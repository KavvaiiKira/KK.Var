using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;

namespace KK.Var.Services.Implementations;

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
            return await LoadCoreAsync(cancellationToken);
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
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveRemoteMachineArchitectureAsync(
        string architecture,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var settings = await LoadCoreAsync(cancellationToken);
            settings.RemoteMachine.Architecture = architecture;
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveGitHubConnectionAsync(
        string accountLogin,
        DateTimeOffset connectedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountLogin);

        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var settings = await LoadCoreAsync(cancellationToken);
            settings.GitHub.AccountLogin = accountLogin;
            settings.GitHub.ConnectedAtUtc = connectedAtUtc;
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task ClearGitHubConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var settings = await LoadCoreAsync(cancellationToken);
            settings.GitHub = new GitHubSettings();
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static async Task<UserSettings> LoadCoreAsync(
        CancellationToken cancellationToken)
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

    private static async Task SaveCoreAsync(
        UserSettings settings,
        CancellationToken cancellationToken)
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
}
