namespace BatchDownloader.API.Models
{
    public class DownloadRequest
    {
        // Relative to the configured FileSystem:RootPath
        public string? Destination { get; set; }

        // One URL per line in client; serialized as JSON list to this model
        public List<string>? Links { get; set; }

        // How many downloads to run concurrently
        public int Concurrency { get; set; } = 3;

        // Optional throttle bytes per second per download. 0 = unlimited.
        public long ThrottleBytesPerSecond { get; set; } = 0;
    }
}
