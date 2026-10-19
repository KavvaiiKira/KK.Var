using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KK.Var.Services.Implementations;

public sealed class LegacyRemoteMachineMigration(
    DatabaseOptions options,
    IDbContextFactory<AppDbContext> contextFactory,
    IUserSettingsService settingsService,
    ISshPasswordStore passwordStore,
    ILocalizationService localization)
{
    private static readonly Guid LegacyMachineId = new Guid("853be76b-5fc9-4514-a297-1ec61cac0214");
    private string? _backupDirectory;

    private void BackupSettings(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var source in new[] { DatabasePaths.UserSettingsFilePath, DatabasePaths.SshPasswordFilePath })
        {
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)), overwrite: false);
            }
        }
        _backupDirectory = directory;
    }

    private RemoteMachine CreateMachine(RemoteMachineSettings connection) => new RemoteMachine
    {
        Id = LegacyMachineId,
        Name = localization.Get("Основная машина"),
        Host = connection.Host?.Trim() ?? string.Empty,
        Port = connection.Port,
        UserName = connection.UserName?.Trim() ?? string.Empty,
        AuthenticationMethod = connection.AuthenticationMethod,
        PrivateKeyPath = connection.PrivateKeyPath,
        HostKeyFingerprint = connection.HostKeyFingerprint,
        Architecture = ArchitectureCompatibility.Normalize(connection.Architecture),
        IsVerified = !string.IsNullOrWhiteSpace(connection.HostKeyFingerprint) &&
            (connection.AuthenticationMethod != "Пароль" || !string.IsNullOrWhiteSpace(connection.Password)) &&
            !string.IsNullOrWhiteSpace(connection.Architecture) &&
            connection.HostKeyHost == connection.Host && connection.HostKeyPort == connection.Port,
    };

    public async Task UpgradeDatabaseIfNeededAsync()
    {
        var originalPath = DatabasePaths.GetDatabaseFilePath(options.FileName);
        if (!File.Exists(originalPath))
        {
            return;
        }
        var settings = await settingsService.LoadAsync();
        localization.SetLanguage(settings.Language);
        var connection = settings.LegacyRemoteMachine ?? new RemoteMachineSettings();
        var machine = CreateMachine(connection);
        var backupDirectory = Path.Combine(DatabasePaths.RecoveryDirectory, "machine-profiles-" + Guid.NewGuid().ToString("N"));
        var replacementPath = originalPath + ".machine-profiles-new";
        await using (var original = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = originalPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString()))
        {
            await original.OpenAsync();
            using var inspect = original.CreateCommand();
            inspect.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='KKProjects'";
            if (Convert.ToInt32(await inspect.ExecuteScalarAsync()) == 0)
            {
                return;
            }
            inspect.CommandText = "SELECT count(*) FROM pragma_table_info('KKProjects') WHERE name='RemoteMachineId'";
            if (Convert.ToInt32(await inspect.ExecuteScalarAsync()) != 0)
            {
                return;
            }
            // Keep a consistent SQLite backup, including committed WAL contents.
            Directory.CreateDirectory(backupDirectory);
            BackupSettings(backupDirectory);
            await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(backupDirectory, options.FileName), Pooling = false,
            }.ToString());
            await backup.OpenAsync();
            original.BackupDatabase(backup);
            inspect.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            await inspect.ExecuteNonQueryAsync();
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            if (File.Exists(replacementPath + suffix))
            {
                File.Move(replacementPath + suffix, Path.Combine(backupDirectory, "previous-incomplete.db" + suffix));
            }
        }
        var replacementOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = replacementPath, Pooling = false }.ToString()).Options;
        await using (var db = new AppDbContext(replacementOptions))
        {
            await db.Database.EnsureCreatedAsync();
            db.RemoteMachines.Add(machine);
            await db.SaveChangesAsync();
            await db.Database.OpenConnectionAsync();
            var sql = (SqliteConnection)db.Database.GetDbConnection();
            using var attach = sql.CreateCommand();
            attach.CommandText = "ATTACH DATABASE $path AS legacy";
            attach.Parameters.AddWithValue("$path", Path.Combine(backupDirectory, options.FileName));
            await attach.ExecuteNonQueryAsync();
            await using var transaction = await sql.BeginTransactionAsync();
            foreach (var table in new[] { "KKProjects", "KKProjectEnvironmentVariables", "KKProjectVersions", "KKProjectDeployments" })
            {
                var sourceColumns = await ColumnsAsync(sql, "legacy", table, (SqliteTransaction)transaction);
                var targetColumns = await ColumnsAsync(sql, "main", table, (SqliteTransaction)transaction);
                if (sourceColumns.Count == 0)
                {
                    continue;
                }
                var columns = targetColumns.Where(sourceColumns.Contains).ToList();
                var expressions = columns.Select(Quote).ToList();
                void Add(string name, string expression)
                {
                    if (targetColumns.Contains(name) && !columns.Contains(name))
                    {
                        columns.Add(name);
                        expressions.Add(expression);
                    }
                }
                if (table is "KKProjects" or "KKProjectDeployments")
                {
                    Add("RemoteMachineId", "$machineId");
                }
                if (table == "KKProjectVersions")
                {
                    // Older versions did not record the build target; never guess compatibility.
                    Add("TargetArchitecture", "'unknown'");
                }
                if (table == "KKProjectDeployments")
                {
                    Add("RemoteMachineName", "$name");
                    Add("RemoteHost", "$host");
                    Add("RemotePort", "$port");
                    Add("RemoteUserName", "$user");
                    Add("RemoteArchitecture", "$architecture");
                    Add("RemoteServerGeneration", "0");
                    Add("RemoteServiceNameSnapshot", "(SELECT p.RemoteServiceName FROM legacy.KKProjects p WHERE p.Id = legacy.KKProjectDeployments.KKProjectId)");
                    Add("RemoteDirectorySnapshot", "(SELECT p.RemoteDeploymentDirectory FROM legacy.KKProjects p WHERE p.Id = legacy.KKProjectDeployments.KKProjectId)");
                    Add("RemoteExecutableSnapshot", "(SELECT p.RemoteExecutableFileName FROM legacy.KKProjects p WHERE p.Id = legacy.KKProjectDeployments.KKProjectId)");
                }
                using var copy = sql.CreateCommand();
                copy.Transaction = (SqliteTransaction)transaction;
                copy.CommandText = $"INSERT INTO main.{Quote(table)} ({string.Join(',', columns.Select(Quote))}) SELECT {string.Join(',', expressions)} FROM legacy.{Quote(table)}";
                copy.Parameters.AddWithValue("$machineId", LegacyMachineId.ToString().ToUpperInvariant());
                copy.Parameters.AddWithValue("$architecture", machine.Architecture ?? "unknown");
                copy.Parameters.AddWithValue("$name", machine.Name);
                copy.Parameters.AddWithValue("$host", machine.Host);
                copy.Parameters.AddWithValue("$port", machine.Port);
                copy.Parameters.AddWithValue("$user", machine.UserName);
                await copy.ExecuteNonQueryAsync();
            }
            using var history = sql.CreateCommand();
            history.Transaction = (SqliteTransaction)transaction;
            history.CommandText = "CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY, ProductVersion TEXT NOT NULL); INSERT INTO __EFMigrationsHistory VALUES ($migration, $version)";
            history.Parameters.AddWithValue("$migration", db.Database.GetMigrations().Single());
            history.Parameters.AddWithValue("$version", db.Model.GetProductVersion() ?? "10.0.11");
            await history.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            using var check = sql.CreateCommand();
            check.CommandText = "PRAGMA foreign_key_check";
            await using (var reader = await check.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    throw new IOException(localization.Get("Перенос базы остановлен: нарушены связи данных."));
                }
            }
            check.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            await check.ExecuteNonQueryAsync();
        }
        SqliteConnection.ClearAllPools();
        // Replace atomically; the original file and a consistent backup both remain recoverable.
        File.Replace(replacementPath, originalPath, Path.Combine(backupDirectory, "original.db"));
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static async Task<HashSet<string>> ColumnsAsync(SqliteConnection connection, string schema, string table, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA {schema}.table_info({Quote(table)})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    public async Task MigrateSettingsAsync()
    {
        var settings = await settingsService.LoadAsync();
        localization.SetLanguage(settings.Language);
        if (settings.LegacyRemoteMachine is not { } legacy || string.IsNullOrWhiteSpace(legacy.Host))
        {
            if (File.Exists(DatabasePaths.SshPasswordFilePath))
            {
                await using var existing = await contextFactory.CreateDbContextAsync();
                if (await existing.RemoteMachines.AnyAsync(machine => machine.Id == LegacyMachineId))
                {
                    var previous = await passwordStore.LoadAsync();
                    if (previous is not null && await passwordStore.LoadAsync(LegacyMachineId) == previous)
                    {
                        await passwordStore.DeleteAsync();
                    }
                }
            }
            return;
        }
        var password = legacy.Password;
        if (_backupDirectory is null)
        {
            BackupSettings(Path.Combine(DatabasePaths.RecoveryDirectory, "machine-settings-" + Guid.NewGuid().ToString("N")));
        }
        if (legacy.AuthenticationMethod == "Пароль" && File.Exists(DatabasePaths.SshPasswordFilePath) && password is null)
        {
            throw new IOException(localization.Get("Не удалось прочитать старый SSH-пароль. Исходные настройки сохранены."));
        }
        if (password is not null)
        {
            await passwordStore.SaveAsync(LegacyMachineId, password);
            if (await passwordStore.LoadAsync(LegacyMachineId) != password)
            {
                throw new IOException(localization.Get("Не удалось проверить перенос SSH-пароля. Исходные настройки сохранены."));
            }
        }
        await using var db = await contextFactory.CreateDbContextAsync();
        if (!await db.RemoteMachines.AnyAsync(machine => machine.Id == LegacyMachineId))
        {
            db.RemoteMachines.Add(CreateMachine(legacy));
            await db.SaveChangesAsync();
        }
        await settingsService.ClearLegacyRemoteMachineAsync();
        await passwordStore.DeleteAsync();
    }
}
