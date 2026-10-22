using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Data;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class SshPasswordStore : ISshPasswordStore
    {
        private const string PasswordEntropyPrefix = "KK.Var.SSH.Password.v2:";

        private const string PasswordFilePrefix = "ssh-";

        private const string PasswordFileExtension = ".dat";

        private static byte[] PasswordEntropy(Guid id) =>
            Encoding.UTF8.GetBytes($"{PasswordEntropyPrefix}{id:N}");

        private static string PasswordPath(Guid id) =>
            Path.Combine(DatabasePaths.UserDataDirectory, $"{PasswordFilePrefix}{id:N}{PasswordFileExtension}");

        public async Task SaveAsync(
            Guid machineId,
            string password,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    Texts.ProtectedSSHPasswordStorageOnlyAvailableWindows);
            }

            DatabasePaths.EnsureUserDataDirectory();

            var encryptedPassword = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password),
                PasswordEntropy(machineId),
                DataProtectionScope.CurrentUser);

            var temporaryPath = PasswordPath(machineId) + ApplicationConstants.TemporaryFileExtension;

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
                throw new PlatformNotSupportedException(Texts.ProtectedSSHPasswordStorageOnlyAvailableWindows);
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
}
