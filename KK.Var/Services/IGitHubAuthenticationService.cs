using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IGitHubAuthenticationService
{
    Task<GitHubToken?> GetTokenAsync(
        CancellationToken cancellationToken = default);
}
