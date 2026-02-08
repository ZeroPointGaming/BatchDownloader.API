
using BatchDownloader.API.Models;
using System.Text.Json.Serialization;

namespace BatchDownloader.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton<Services.IFileSystemService, Services.FileSystemService>();
            builder.Services.AddSingleton<Services.IDownloadService, Services.DownloadService>();
            builder.Services.AddHttpClient();
            builder.Services.AddAuthorization();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.SetIsOriginAllowed(origin => true)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            // Add services to the container.
            var app = builder.Build();

            // Handle Private Network Access (PNA) preflight requests
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
                {
                    context.Response.Headers.Append("Access-Control-Allow-Private-Network", "true");
                }
                
                await next();
            });

            app.UseCors("AllowAll");
            app.UseWebSockets();

            // Simple API Key Auth Middleware
            app.Use(async (context, next) =>
            {
                var pathValue = context.Request.Path.Value ?? "";
                if (context.Request.Method == "OPTIONS" || 
                    pathValue.Contains("/health", StringComparison.OrdinalIgnoreCase) ||
                    pathValue.Contains("/ws", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                // Check for X-API-KEY header
                if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync($"API Key header (X-API-KEY) was not provided. Path: {context.Request.Path}");
                    return;
                }

                var appSettings = context.RequestServices.GetRequiredService<IConfiguration>();
                var apiKey = appSettings.GetValue<string>("ApiKey");

                if (!string.Equals(extractedApiKey, apiKey))
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized: The provided API Key is incorrect.");
                    return;
                }

                await next();
            });

            app.UseAuthorization();

            // Routing gets declared here
            app.MapGet("/getDownloadDirectory", (Services.IFileSystemService fsSvc) =>
            {
                var downloadDir = fsSvc.GetRootPath();
                return Results.Ok(new { downloadDir });
            });

            app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "1.1.3" }));

            app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
            {
                // Give it a tiny bit of time to return the response before killing
                Task.Run(async () =>
                {
                    await Task.Delay(500);
                    lifetime.StopApplication();
                });
                return Results.Ok(new { message = "Shutting down..." });
            });

            app.Map("/ws", async (HttpContext context, Services.IDownloadService downloadSvc) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    using var ws = await context.WebSockets.AcceptWebSocketAsync();
                    await downloadSvc.SubscribeWebSocket(ws);
                }
                else
                {
                    context.Response.StatusCode = 400;
                }
            });

            app.MapGet("/filesystem/exists/{*path}", (string path, Services.IFileSystemService fsSvc) =>
            {
                var decodedUrl = Uri.UnescapeDataString(path);
                
                try 
                {
                    var exists = fsSvc.DirectoryExists(decodedUrl);
                    return Results.Ok(new { exists });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            app.MapPost("/downloads", async (DownloadRequest req, Services.IDownloadService downloadSvc, Services.IFileSystemService fsSvc) =>
            {
                if (req == null || req.Links == null || req.Links.Count == 0)
                    return Results.BadRequest(new { error = "No Links Provided" });

                string fullDest;
                try
                {
                    fullDest = fsSvc.ResolveAndValidateRelativePath(req.Destination ?? string.Empty);
                }
                catch (Exception e)
                {
                    return Results.BadRequest(new { error = e.Message });
                }

                if (!Directory.Exists(fullDest))
                    return Results.BadRequest(new { error = "Destination directory does not exist." });

                var map = await downloadSvc.StartDownloadsAsync(req, fullDest);
                return Results.Ok(map);
            });

            app.Run();
        }
    }
}