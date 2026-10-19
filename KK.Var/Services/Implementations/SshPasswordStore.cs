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
    private static readonly byte[] AdditionalEntropy = Encoding.UTF8.GetBytes("KK.Var.SSH.Password.v1");

    private static byte[] PasswordEntropy(Guid id) => id == Guid.Empty ? AdditionalEntropy :
        Encoding.UTF8.GetBytes($"KK.Var.SSH.Password.v2:{id:N}");

    public Task SaveAsync(string password, CancellationToken cancellationToken = default) => SaveAsync(Guid.Empty, password, cancellationToken);
    public Task<string?> LoadAsync(CancellationToken cancellationToken = default) => LoadAsync(Guid.Empty, cancellationToken);
    public Task DeleteAsync(CancellationToken cancellationToken = default) => DeleteAsync(Guid.Empty, cancellationToken);

    private static string PasswordPath(Guid id) => id == Guid.Empty ? DatabasePaths.SshPasswordFilePath :
        Path.Combine(DatabasePaths.UserDataDirectory, $"ssh-{id:N}.dat");

    public async Task SaveAsync(
        Guid machineId,
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
            PasswordEntropy(machineId),
            DataProtectionScope.CurrentUser);

        var temporaryPath = PasswordPath(machineId) + ".tmp";

        await File.WriteAllBytesAsync(
            temporaryPath,
            encryptedPassword,
            cancellationToken);

        File.Move(
            temporaryPath,
            PasswordPath(machineId),
            overwrite: true);
    }

    public async Task<string?> LoadAsync(
        Guid machineId,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Защищённое хранение SSH-пароля реализовано только для Windows.");
        }

        if (!File.Exists(PasswordPath(machineId)))
        {
            return null;
        }

        try
        {
            var encryptedPassword = await File.ReadAllBytesAsync(
                PasswordPath(machineId),
                cancellationToken);

            var password = ProtectedData.Unprotect(
                encryptedPassword,
                PasswordEntropy(machineId),
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(password);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public Task DeleteAsync(Guid machineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(PasswordPath(machineId)))
        {
            File.Delete(PasswordPath(machineId));
        }

        return Task.CompletedTask;
    }
}
