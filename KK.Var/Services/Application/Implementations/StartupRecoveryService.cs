using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Data;
using KK.Var.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class StartupRecoveryService(
        DatabaseOptions databaseOptions,
        IDbContextFactory<AppDbContext> contextFactory,
        ILocalizationService localizationService,
        string? userDataDirectory = null) : IStartupRecoveryService
    {
        private string UserDataDirectory => userDataDirectory ?? DatabasePaths.UserDataDirectory;

        private string UserSettingsFilePath => Path.Combine(UserDataDirectory, ApplicationConstants.SettingsFileName);

        private const string BackupTimestampFormat = "yyyyMMdd-HHmmss-fff";

        private const string WalFileSuffix = "-wal";

        private const string SharedMemoryFileSuffix = "-shm";

        public async Task<string> BackupAndResetAsync(
            StartupRecoveryKind kind,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recoveryDirectory = Path.Combine(
                Path.Combine(UserDataDirectory, ApplicationConstants.RecoveryDirectoryName),
                DateTime.UtcNow.ToString(BackupTimestampFormat));
            Directory.CreateDirectory(recoveryDirectory);

            switch (kind)
            {
                case StartupRecoveryKind.UserSettings:
                    MoveIfExists(UserSettingsFilePath, recoveryDirectory);
                    MoveIfExists(UserSettingsFilePath + ApplicationConstants.TemporaryFileExtension, recoveryDirectory);
                    break;
                case StartupRecoveryKind.Database:
                    var databasePath = Path.Combine(UserDataDirectory, Path.GetFileName(DatabasePaths.GetDatabaseFilePath(databaseOptions.FileName)));
                    SqliteConnection.ClearAllPools();
                    MoveIfExists(databasePath, recoveryDirectory);
                    MoveIfExists(databasePath + WalFileSuffix, recoveryDirectory);
                    MoveIfExists(databasePath + SharedMemoryFileSuffix, recoveryDirectory);
                    await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken))
                    {
                        await db.Database.MigrateAsync(cancellationToken);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }

            return recoveryDirectory;
        }

        public void OpenUserDataDirectory()
        {
            Directory.CreateDirectory(UserDataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = UserDataDirectory,
                UseShellExecute = true,
            });
        }

        private void MoveIfExists(string sourcePath, string recoveryDirectory)
        {
            if (!File.Exists(sourcePath))
            {
                return;
            }

            var source = new FileInfo(sourcePath);
            if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    localizationService.Format(Texts.RecoverySourceCannotReparsePoint, sourcePath));
            }

            var destination = Path.Combine(recoveryDirectory, source.Name);
            File.Move(source.FullName, destination);
        }
    }
}
