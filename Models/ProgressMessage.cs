namespace BatchDownloader.API.Models
{
    public class ProgressMessage
    {
        public int Id { get; set; }
        public string? Url { get; set; }
        public long? BytesReceived { get; set; }
        public long? TotalBytes { get; set; }
        public string? Status { get; set; } // downloading, completed, cancelled, error
        public string? LocalPath { get; set; }
        public string? Error { get; set; }
    }
}
