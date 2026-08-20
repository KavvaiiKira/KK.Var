using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace KK.Var.Services.Implementations;

public sealed class StartupRecoveryService(
    DatabaseOptions databaseOptions,
    IDbContextFactory<AppDbContext> contextFactory) : IStartupRecoveryService
{
    public async Task<string> BackupAndResetAsync(
        StartupRecoveryKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recoveryDirectory = Path.Combine(
            DatabasePaths.RecoveryDirectory,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(recoveryDirectory);

        switch (kind)
        {
            case StartupRecoveryKind.UserSettings:
                MoveIfExists(DatabasePaths.UserSettingsFilePath, recoveryDirectory);
                MoveIfExists(DatabasePaths.UserSettingsFilePath + ".tmp", recoveryDirectory);
                MoveIfExists(DatabasePaths.SshPasswordFilePath, recoveryDirectory);
                break;
            case StartupRecoveryKind.Database:
                var databasePath = DatabasePaths.GetDatabaseFilePath(databaseOptions.FileName);
                SqliteConnection.ClearAllPools();
                MoveIfExists(databasePath, recoveryDirectory);
                MoveIfExists(databasePath + "-wal", recoveryDirectory);
                MoveIfExists(databasePath + "-shm", recoveryDirectory);
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
        DatabasePaths.EnsureUserDataDirectory();
        Process.Start(new ProcessStartInfo
        {
            FileName = DatabasePaths.UserDataDirectory,
            UseShellExecute = true,
        });
    }

    private static void MoveIfExists(string sourcePath, string recoveryDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var source = new FileInfo(sourcePath);
        if ((source.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Recovery source cannot be a reparse point: {sourcePath}");
        }

        var destination = Path.Combine(recoveryDirectory, source.Name);
        File.Move(source.FullName, destination);
    }
}
