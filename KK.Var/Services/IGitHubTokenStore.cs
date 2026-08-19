using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services;

public interface IGitHubTokenStore
{
    Task SaveAsync(string token, CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
