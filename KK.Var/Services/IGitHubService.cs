using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IGitHubService
    {
        Task<IReadOnlyList<string>> GetBranchesAsync(
            string repositoryFullName,
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<GitHubCommit> GetBranchCommitAsync(
            string repositoryFullName,
            string? branch,
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<GitHubCommit> GetCommitAsync(
            string repositoryFullName,
            string commitSha,
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<GitHubDeviceAuthorization> StartDeviceAuthorizationAsync(
            CancellationToken cancellationToken = default);

        Task<GitHubToken> WaitForAccessTokenAsync(
            GitHubDeviceAuthorization authorization,
            CancellationToken cancellationToken = default);

        Task<GitHubToken> RefreshAccessTokenAsync(
            GitHubToken token,
            CancellationToken cancellationToken = default);

        Task<GitHubUser> GetCurrentUserAsync(
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<Stream> DownloadRepositoryArchiveAsync(
            string repositoryFullName,
            string commitSha,
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<string> GetDefaultBranchCommitShaAsync(
            string repositoryFullName,
            string accessToken,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GitHubSubmodule>> GetSubmodulesAsync(
            string repositoryFullName,
            string commitSha,
            string accessToken,
            CancellationToken cancellationToken = default);
    }
}
