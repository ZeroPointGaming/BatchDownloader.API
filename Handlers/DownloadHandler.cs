using System.IO;
using System.Threading.Tasks;
using BatchDownloader.API.Models;
using BatchDownloader.API.Services;

namespace BatchDownloader.API.Handlers
{
    public class DownloadHandler
    {
        private readonly IDownloadService _downloadSvc;
        private readonly IFileSystemService _fsSvc;

        public DownloadHandler(IDownloadService downloadSvc, IFileSystemService fsSvc)
        {
            _downloadSvc = downloadSvc;
            _fsSvc = fsSvc;
        }

        public async Task<IResult> StartDownloadsAsync(DownloadRequest req)
        {
            if (req == null || req.Links == null || req.Links.Count == 0)
                return Results.BadRequest(new { error = "No links provided." });

            string fullDest;
            try
            {
                fullDest = _fsSvc.ResolveAndValidateRelativePath(req.Destination ?? string.Empty);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (!Directory.Exists(fullDest))
                return Results.BadRequest(new { error = "Destination does not exist." });

            var map = await _downloadSvc.StartDownloadsAsync(req, fullDest);
            return Results.Ok(map);
        }
    }
}