using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Data;

namespace KK.Var.Services.Implementations;

public sealed class SshPasswordStore : ISshPasswordStore
{
    private static readonly byte[] AdditionalEntropy =
        Encoding.UTF8.GetBytes("KK.Var.SSH.Password.v1");

    public async Task SaveAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Защищённое хранение SSH-пароля реализовано только для Windows.");
        }

        DatabasePaths.EnsureUserDataDirectory();

        var encryptedPassword = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            AdditionalEntropy,
            DataProtectionScope.CurrentUser);
        var temporaryPath = DatabasePaths.SshPasswordFilePath + ".tmp";

        await File.WriteAllBytesAsync(
            temporaryPath,
            encryptedPassword,
            cancellationToken);
        File.Move(
            temporaryPath,
            DatabasePaths.SshPasswordFilePath,
            overwrite: true);
    }

    public async Task<string?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Защищённое хранение SSH-пароля реализовано только для Windows.");
        }

        if (!File.Exists(DatabasePaths.SshPasswordFilePath))
        {
            return null;
        }

        try
        {
            var encryptedPassword = await File.ReadAllBytesAsync(
                DatabasePaths.SshPasswordFilePath,
                cancellationToken);
            var password = ProtectedData.Unprotect(
                encryptedPassword,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(password);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(DatabasePaths.SshPasswordFilePath))
        {
            File.Delete(DatabasePaths.SshPasswordFilePath);
        }

        return Task.CompletedTask;
    }
}
