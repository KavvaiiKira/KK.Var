using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IGitHubTokenStore
{
    Task SaveAsync(GitHubToken token, CancellationToken cancellationToken = default);

    Task<GitHubToken?> LoadAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
