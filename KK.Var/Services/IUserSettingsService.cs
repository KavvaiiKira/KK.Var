using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;

namespace KK.Var.Services
{
    public interface IUserSettingsService
    {
        Task SaveDeploymentNotificationsEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

        Task<UserSettings> LoadAsync(
            CancellationToken cancellationToken = default);

        Task SaveAsync(
            UserSettings settings,
            CancellationToken cancellationToken = default);

        Task SaveGitHubConnectionAsync(
            string accountLogin,
            DateTimeOffset connectedAtUtc,
            CancellationToken cancellationToken = default);

        Task ClearGitHubConnectionAsync(
            CancellationToken cancellationToken = default);
    }
}
