using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;

namespace KK.Var.Services;

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<UserSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(DatabasePaths.UserSettingsFilePath))
            {
                return new UserSettings();
            }

            await using var stream = File.OpenRead(DatabasePaths.UserSettingsFilePath);

            return await JsonSerializer.DeserializeAsync<UserSettings>(
                       stream,
                       SerializerOptions,
                       cancellationToken)
                   ?? new UserSettings();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            DatabasePaths.EnsureUserDataDirectory();

            var temporaryPath = DatabasePaths.UserSettingsFilePath + ".tmp";

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                DatabasePaths.UserSettingsFilePath,
                overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
