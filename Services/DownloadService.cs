using BatchDownloader.API.Models;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BatchDownloader.API.Services
{
    public interface IDownloadService
    {
        Task<Dictionary<int, string>> StartDownloadsAsync(DownloadRequest request, string absoluteDestination);
        Task SubscribeWebSocket(WebSocket ws);
    }

    public class DownloadService : IDownloadService, IDisposable
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _cancellations = new();
        private readonly ConcurrentDictionary<int, string> _urls = new();
        private readonly ConcurrentDictionary<int, (string dest, long throttle)> _metadata = new();
        private readonly ConcurrentDictionary<int, ProgressMessage> _lastProgress = new();
        private readonly SemaphoreSlim _idLock = new(1, 1);
        private int _nextId = 1;

        private int _maxTimeoutSeconds = 600;

        // Events subscribed by websocket handler
        private readonly List<Func<ProgressMessage, Task>> _progressConsumers = new();
        private readonly object _consumersLock = new();

        public DownloadService(IHttpClientFactory httpFactory, IConfiguration config)
        {
            _httpFactory = httpFactory;

            // Read numeric config safely. Use 600 seconds as a sensible default.
            _maxTimeoutSeconds = config.GetValue<int>("MaxDownloadTimeout:MaxTimeout", 600);
            if (_maxTimeoutSeconds <= 0)
            {
                // enforce a minimum positive timeout
                _maxTimeoutSeconds = 600;
            }
        }

        public async Task<Dictionary<int, string>> StartDownloadsAsync(DownloadRequest request, string absDest)
        {
            var map = new Dictionary<int, string>();
            var concurrency = Math.Max(1, request.Concurrency);
            var throttle = Math.Max(0, request.ThrottleBytesPerSecond);

            var semaphore = new SemaphoreSlim(concurrency, concurrency);
            var tasks = new List<Task>();

            if (request.Links == null)
                return new Dictionary<int, string>(); // Exit early if no links provided.

            foreach (var url in request.Links)
            {
                var id = await ReserveIdAsync();
                _urls[id] = url;
                _metadata[id] = (absDest, throttle);
                _cancellations[id] = new CancellationTokenSource();
                map[id] = url;

                // Immediately report as pending
                await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "pending" });

                tasks.Add(Task.Run(async () =>
                {
                    var ct = _cancellations[id].Token;
                    if (ct.IsCancellationRequested) return;

                    await semaphore.WaitAsync(ct);
                    try
                    {
                        if (ct.IsCancellationRequested) return;
                        await DownloadSingleAsync(id, url, absDest, throttle, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "stopped" });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            return map;
        }

        private void ResumeInternalTask(int id, string url, string dest, long throttle, CancellationToken ct)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await DownloadSingleAsync(id, url, dest, throttle, ct);
                }
                catch (OperationCanceledException)
                {
                    await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "stopped" });
                }
            });
        }

        private async Task<int> ReserveIdAsync()
        {
            await _idLock.WaitAsync();
            try
            {
                return _nextId++;
            }
            finally
            {
                _idLock.Release();
            }
        }

        private async Task DownloadSingleAsync(int id, string url, string destRoot, long throttle, CancellationToken ct)
        {
            var http = _httpFactory.CreateClient("download");
            http.Timeout = TimeSpan.FromSeconds(_maxTimeoutSeconds);

            try
            {
                // 1. Initial Head or dummy Get to find out filename if not known, 
                // but let's just use the URL filename for simplicity or get it from first response.
                var tempUri = new Uri(url);
                var fileName = Path.GetFileName(tempUri.LocalPath);
                var destPath = Path.Combine(destRoot, fileName);

                long existingLength = 0;
                if (File.Exists(destPath))
                {
                    existingLength = new FileInfo(destPath).Length;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existingLength > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(existingLength, null);
                }

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                
                // If we get 416 (Requested Range Not Satisfiable), it might mean the file is already complete or server changed.
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "completed", LocalPath = destPath });
                    return;
                }

                response.EnsureSuccessStatusCode();

                var isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                var mode = isPartial ? FileMode.Append : FileMode.Create;
                
                // Update filename if server provides Content-Disposition
                var actualFileName = GetFileNameFromResponse(response) ?? fileName;
                if (actualFileName != fileName)
                {
                    destPath = Path.Combine(destRoot, actualFileName);
                    // Re-evaluate length if filename changed? (Unlikely for same URL, but for safety:)
                    if (File.Exists(destPath)) existingLength = new FileInfo(destPath).Length;
                }

                await using var fs = new FileStream(destPath, mode, FileAccess.Write, FileShare.None, 81920, true);

                long recieved = isPartial ? existingLength : 0;
                var total = (response.Content.Headers.ContentLength ?? 0) + recieved;
                
                var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[81920];
                var sw = System.Diagnostics.Stopwatch.StartNew();

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;

                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                    recieved += read;

                    // Throttle
                    if (throttle > 0)
                    {
                        var expectedSeconds = (double)recieved / throttle;
                        var elapsedSeconds = sw.Elapsed.TotalSeconds;
                        if (expectedSeconds > elapsedSeconds)
                        {
                            var delayMs = (int)Math.Ceiling((expectedSeconds - elapsedSeconds) * 1000);
                            if (delayMs > 0) await Task.Delay(delayMs, ct);
                        }
                    }

                    await PublishProgressAsync(new ProgressMessage
                    {
                        Id = id,
                        Url = url,
                        BytesReceived = recieved,
                        TotalBytes = total,
                        Status = "downloading"
                    });
                }

                await PublishProgressAsync(new ProgressMessage
                {
                    Id = id,
                    Url = url,
                    BytesReceived = recieved,
                    TotalBytes = total,
                    Status = "completed",
                    LocalPath = destPath
                });
            }
            catch (OperationCanceledException)
            {
                await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "stopped" });
            }
            catch (Exception e)
            {
                await PublishProgressAsync(new ProgressMessage { Id = id, Url = url, Status = "error", Error = e.Message });
            }
            finally
            {
                // cleanup cancellation token source
                _cancellations.TryRemove(id, out var _);
            }
        }

        private string? GetFileNameFromResponse(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentDisposition != null && !string.IsNullOrEmpty(response.Content.Headers.ContentDisposition.FileNameStar))
                return response.Content.Headers.ContentDisposition.FileNameStar;
            if (response.Content.Headers.ContentDisposition != null && !string.IsNullOrEmpty(response.Content.Headers.ContentDisposition.FileName))
                return response.Content.Headers.ContentDisposition.FileName.Trim('\"');
            return null;
        }

        private static async Task SafeInvokeConsumerAsync(Func<ProgressMessage, Task> c, ProgressMessage msg)
        {
            try { await c(msg); } catch { /* swallow for now */ }
        }

        private Task PublishProgressAsync(ProgressMessage msg)
        {
            if (msg.Status == "removed")
            {
                _lastProgress.TryRemove(msg.Id, out _);
            }
            else
            {
                _lastProgress[msg.Id] = msg;
            }

            List<Func<ProgressMessage, Task>> consumers;
            lock (_consumersLock)
            {
                consumers = _progressConsumers.ToList();
            }

            var tasks = consumers.Select(c => SafeInvokeConsumerAsync(c, msg));
            return Task.WhenAll(tasks);
        }

        public async Task SubscribeWebSocket(WebSocket ws)
        {
            async Task Consumer(ProgressMessage m)
            {
                if (ws.State != WebSocketState.Open) return;

                var payload = JsonSerializer.Serialize(m);
                var buffer = Encoding.UTF8.GetBytes(payload);

                await ws.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
            }

            lock (_consumersLock) _progressConsumers.Add(Consumer);
            
            // Sync current state to the new client
            foreach (var progress in _lastProgress.Values.OrderBy(p => p.Id))
            {
                await Consumer(progress);
            }

            // Read Loop, Accepts json control messages: { "command": "cancel", "id": 5 } for example.
            var inBuf = new byte[4096];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(inBuf, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    var msg = Encoding.UTF8.GetString(inBuf, 0, result.Count);
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var ctrl = JsonSerializer.Deserialize<ControlMessage>(msg, options);
                        if (ctrl?.Command == "cancel" && ctrl.Id.HasValue)
                        {
                            if (_cancellations.TryGetValue(ctrl.Id.Value, out var cts))
                                cts.Cancel();
                        }
                        else if (ctrl?.Command == "resume" && ctrl.Id.HasValue)
                        {
                            var id = ctrl.Id.Value;
                            if (_urls.TryGetValue(id, out var url) && _metadata.TryGetValue(id, out var meta))
                            {
                                if (!_cancellations.ContainsKey(id))
                                {
                                    var cts = new CancellationTokenSource();
                                    _cancellations[id] = cts;
                                    ResumeInternalTask(id, url, meta.dest, meta.throttle, cts.Token);
                                }
                            }
                        }
                        else if (ctrl?.Command == "remove" && ctrl.Id.HasValue)
                        {
                            var id = ctrl.Id.Value;
                            if (_cancellations.TryGetValue(id, out var cts))
                            {
                                cts.Cancel();
                            }
                            _urls.TryRemove(id, out _);
                            _metadata.TryRemove(id, out _);
                            _cancellations.TryRemove(id, out _);
                            // Optionally send a "removed" status
                            await PublishProgressAsync(new ProgressMessage { Id = id, Url = "", Status = "removed" });
                        }
                        else if (ctrl?.Command == "clear")
                        {
                            var toRemove = _lastProgress.Where(kvp => 
                                kvp.Value.Status == "completed" || 
                                kvp.Value.Status == "stopped" || 
                                kvp.Value.Status == "error"
                            ).Select(kvp => kvp.Key).ToList();

                            foreach (var id in toRemove)
                            {
                                _lastProgress.TryRemove(id, out _);
                                // Notify all clients that these are removed from the synchronized list
                                await PublishProgressAsync(new ProgressMessage { Id = id, Status = "removed" });
                            }
                        }
                    }
                    catch { 
                        // Ignore parse errors...
                    }
                }
            }
            finally
            {
                // Remove the consumer.
                lock (_consumersLock) _progressConsumers.Remove(Consumer);
                if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
        }

        public void Dispose()
        {
            foreach (var c in _cancellations.Values) c.Cancel();
            foreach (var c in _cancellations.Values) c.Dispose();
        }

        private class ControlMessage
        {
            public string? Command { get; set; }
            public int? Id { get; set; }
        }
    }
}