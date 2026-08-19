using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;

namespace KK.Var.Services.Implementations;

public sealed class GitHubTokenStore : IGitHubTokenStore
{
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("KK.Var.GitHub.Token.v1");

    public async Task SaveAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Защищённое хранение GitHub-токена пока реализовано только для Windows.");
        }

        DatabasePaths.EnsureUserDataDirectory();

        var encryptedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token),
            AdditionalEntropy,
            DataProtectionScope.CurrentUser);
        var temporaryPath = DatabasePaths.GitHubTokenFilePath + ".tmp";

        await File.WriteAllBytesAsync(
            temporaryPath,
            encryptedToken,
            cancellationToken);
        File.Move(
            temporaryPath,
            DatabasePaths.GitHubTokenFilePath,
            overwrite: true);
    }

    public async Task<string?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Защищённое хранение GitHub-токена пока реализовано только для Windows.");
        }

        if (!File.Exists(DatabasePaths.GitHubTokenFilePath))
        {
            return null;
        }

        var encryptedToken = await File.ReadAllBytesAsync(
            DatabasePaths.GitHubTokenFilePath,
            cancellationToken);
        var token = ProtectedData.Unprotect(
            encryptedToken,
            AdditionalEntropy,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(token);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(DatabasePaths.GitHubTokenFilePath))
        {
            File.Delete(DatabasePaths.GitHubTokenFilePath);
        }

        return Task.CompletedTask;
    }
}
