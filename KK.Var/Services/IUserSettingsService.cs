using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;

namespace KK.Var.Services;

public interface IUserSettingsService
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        UserSettings settings,
        CancellationToken cancellationToken = default);
}
