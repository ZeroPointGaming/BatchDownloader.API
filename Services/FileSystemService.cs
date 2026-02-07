namespace BatchDownloader.API.Services
{
    public interface IFileSystemService
    {
        bool DirectoryExists(string relPath);
        string ResolveAndValidateRelativePath(string relPath);
        string GetRootPath();
    }

    public class FileSystemService : IFileSystemService
    {
        private readonly string _rootPath;

        public FileSystemService(IConfiguration configuration)
        {
            _rootPath = configuration["FileSystem:RootPath"] ?? throw new ArgumentNullException("FileSystem:RootPath configuration is missing.");
            _rootPath = Path.GetFullPath(_rootPath);
        }

        public bool DirectoryExists(string relPath)
        {
            try 
            {
                var fullPath = ResolveAndValidateRelativePath(relPath);
                return Directory.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }

        public string GetRootPath() => _rootPath;

        public string ResolveAndValidateRelativePath(string relPath)
        {
            // If empty or . use root path
            relPath ??= string.Empty;

            var combined = Path.Combine(_rootPath, relPath);
            var full = Path.GetFullPath(combined);

            if (!full.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Resolved path is outside of the allowed root directory.");
            }

            return full;
        }
    }
}