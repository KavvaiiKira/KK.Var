using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Data;

namespace KK.Var.Services.Implementations
{
    public sealed class UserSettingsService(string? settingsFilePath = null) : IUserSettingsService
    {
        private string SettingsFilePath => settingsFilePath ?? DatabasePaths.UserSettingsFilePath;

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

        public async Task SaveDeploymentNotificationsEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            await _fileLock.WaitAsync(cancellationToken);
            try
            {
                var settings = await LoadCoreAsync(cancellationToken);
                settings.DeploymentNotificationsEnabled = enabled;
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

        private async Task<UserSettings> LoadCoreAsync(
            CancellationToken cancellationToken)
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new UserSettings();
            }

            await using var stream = File.OpenRead(SettingsFilePath);

            return await JsonSerializer.DeserializeAsync<UserSettings>(
                stream,
                SerializerOptions,
                cancellationToken) ??
                       new UserSettings();
        }

        private async Task SaveCoreAsync(
            UserSettings settings,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);

            var temporaryPath = SettingsFilePath + ApplicationConstants.TemporaryFileExtension;

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
                SettingsFilePath,
                overwrite: true);
        }
    }
}
