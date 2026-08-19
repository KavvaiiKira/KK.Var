using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IGitHubService
{
    Task<GitHubDeviceAuthorization> StartDeviceAuthorizationAsync(
        CancellationToken cancellationToken = default);

    Task<string> WaitForAccessTokenAsync(
        GitHubDeviceAuthorization authorization,
        CancellationToken cancellationToken = default);

    Task<GitHubUser> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}
