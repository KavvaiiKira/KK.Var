using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Data;
using System.Text.Json.Serialization;

namespace KK.Var.Services.Implementations;

public sealed class UserSettingsService(ISshPasswordStore sshPasswordStore) : IUserSettingsService
{
    private const string PasswordAuthentication = "Пароль";
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    static UserSettingsService()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

    public async Task<UserSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);

        try
        {
            var settings = await LoadCoreAsync(cancellationToken);

            settings.RemoteMachine.Password = await sshPasswordStore.LoadAsync(
                cancellationToken);

            return settings;
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
            if (settings.RemoteMachine.AuthenticationMethod == PasswordAuthentication &&
                !string.IsNullOrWhiteSpace(settings.RemoteMachine.Password))
            {
                await sshPasswordStore.SaveAsync(
                    settings.RemoteMachine.Password,
                    cancellationToken);
            }
            else
            {
                await sshPasswordStore.DeleteAsync(cancellationToken);
                settings.RemoteMachine.Password = null;
            }

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
                   cancellationToken) ??
                   new UserSettings();
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
