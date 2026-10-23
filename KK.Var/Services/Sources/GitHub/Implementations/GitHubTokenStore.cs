using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Data;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class GitHubTokenStore(string? tokenFilePath = null) : IGitHubTokenStore
    {
        private string TokenFilePath => tokenFilePath ?? DatabasePaths.GitHubTokenFilePath;

        private const string TokenEntropyPurpose = "KK.Var.GitHub.Token.v1";

        private static readonly byte[] AdditionalEntropy =
            Encoding.UTF8.GetBytes(TokenEntropyPurpose);

        public async Task SaveAsync(
            GitHubToken token,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(token);
            ArgumentException.ThrowIfNullOrWhiteSpace(token.AccessToken);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    Texts.ProtectedGitHubTokenStorageOnlyAvailableWindows);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TokenFilePath)!);

            var encryptedToken = ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(token),
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            var temporaryPath = TokenFilePath + ApplicationConstants.TemporaryFileExtension;

            await File.WriteAllBytesAsync(
                temporaryPath,
                encryptedToken,
                cancellationToken);

            File.Move(
                temporaryPath,
                TokenFilePath,
                overwrite: true);
        }

        public async Task<GitHubToken?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    Texts.ProtectedGitHubTokenStorageOnlyAvailableWindows);
            }

            if (!File.Exists(TokenFilePath))
            {
                return null;
            }

            var encryptedToken = await File.ReadAllBytesAsync(
                TokenFilePath,
                cancellationToken);

            var token = ProtectedData.Unprotect(
                encryptedToken,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<GitHubToken>(token) ??
                throw new InvalidDataException(Texts.GitHubTokenDataInvalid);
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(TokenFilePath))
            {
                File.Delete(TokenFilePath);
            }

            return Task.CompletedTask;
        }
    }
}
