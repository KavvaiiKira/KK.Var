using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

public sealed class GitHubTokenStore : IGitHubTokenStore
{
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("KK.Var.GitHub.Token.v1");

    public async Task SaveAsync(
        GitHubToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.AccessToken);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Защищённое хранение GitHub-токена пока реализовано только для Windows.");
        }

        DatabasePaths.EnsureUserDataDirectory();

        var encryptedToken = ProtectedData.Protect(
            JsonSerializer.SerializeToUtf8Bytes(token),
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

    public async Task<GitHubToken?> LoadAsync(
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

        return JsonSerializer.Deserialize<GitHubToken>(token)
            ?? throw new InvalidDataException("GitHub token data is invalid.");
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
