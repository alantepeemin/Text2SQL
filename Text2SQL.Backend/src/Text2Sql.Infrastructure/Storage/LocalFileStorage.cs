using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Storage
{
    /// <summary>
    /// FAZ 4: Yerel disk depolama. Kök: Storage:ResultsRoot (vars. "Data/Results").
    /// Path traversal koruması: göreli yollar kökün dışına çıkamaz.
    /// </summary>
    public sealed class LocalFileStorage : IFileStorage
    {
        private readonly string _root;

        public LocalFileStorage(IWebHostEnvironment env, IConfiguration configuration)
        {
            var configuredRoot = configuration.GetValue<string>("Storage:ResultsRoot") ?? "Data/Results";
            _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, configuredRoot));
        }

        public async Task<string> SaveTextAsync(string relativeDirectory, string fileName, string content, CancellationToken ct = default)
        {
            var relativePath = Path.Combine(relativeDirectory, fileName);
            var fullPath = ResolveSafe(relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, content, ct);

            // Yolu her zaman '/' ile normalize et (OS bağımsız kayıt)
            return relativePath.Replace('\\', '/');
        }

        public async Task<string?> ReadTextAsync(string relativePath, CancellationToken ct = default)
        {
            var fullPath = ResolveSafe(relativePath);
            if (!File.Exists(fullPath)) return null;
            return await File.ReadAllTextAsync(fullPath, ct);
        }

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            var fullPath = ResolveSafe(relativePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            return Task.CompletedTask;
        }

        public Task DeleteDirectoryAsync(string relativeDirectory, CancellationToken ct = default)
        {
            var fullPath = ResolveSafe(relativeDirectory);
            if (Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
            return Task.CompletedTask;
        }

        private string ResolveSafe(string relativePath)
        {
            var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
            if (!fullPath.StartsWith(_root, StringComparison.Ordinal))
                throw new InvalidOperationException("Geçersiz depolama yolu."); // traversal denemesi
            return fullPath;
        }
    }
}
