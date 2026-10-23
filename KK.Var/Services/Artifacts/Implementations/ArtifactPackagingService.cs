using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactPackagingService : IArtifactPackagingService
    {
        public async Task CreateTarGzAsync(
            string sourceDirectory,
            Stream destination,
            CancellationToken cancellationToken)
        {
            await using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true);
            await using var writer = new TarWriter(gzip, leaveOpen: false);

            foreach (var path in Directory.EnumerateFileSystemEntries(
                sourceDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/');
                writer.WriteEntry(path, relative);
            }
        }

        public async Task<string> CalculateSha256Async(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexStringLower(hash);
        }
    }
}
