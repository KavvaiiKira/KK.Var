using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

public sealed class GitHubAuthenticationService(
    IGitHubService gitHubService,
    IGitHubTokenStore tokenStore) : IGitHubAuthenticationService
{
    private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);

    public async Task<GitHubToken?> GetTokenAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.LoadAsync(cancellationToken);
        if (token?.AccessTokenExpiresAtUtc is null ||
            token.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return token;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            token = await tokenStore.LoadAsync(cancellationToken);
            if (token?.AccessTokenExpiresAtUtc is null ||
                token.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return token;
            }

            if (string.IsNullOrWhiteSpace(token.RefreshToken) ||
                token.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                await tokenStore.DeleteAsync(cancellationToken);
                return null;
            }

            try
            {
                var refreshed = await gitHubService.RefreshAccessTokenAsync(
                    token,
                    cancellationToken);

                await tokenStore.SaveAsync(refreshed, cancellationToken);

                return refreshed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                await tokenStore.DeleteAsync(cancellationToken);
                return null;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
