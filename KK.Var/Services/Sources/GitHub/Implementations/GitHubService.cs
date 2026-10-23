using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Models;
using Microsoft.Extensions.Logging;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed partial class GitHubService : IGitHubService, IDisposable
    {
        private const string NonJsonErrorResponseMessage = "GitHub error response was not JSON; using the HTTP status message.";

        private static readonly Uri DeviceCodeUri =
            new Uri(GitHubProtocol.DeviceCodeEndpoint);

        private static readonly Uri AccessTokenUri =
            new Uri(GitHubProtocol.AccessTokenEndpoint);

        private static readonly Uri CurrentUserUri =
            new Uri(GitHubProtocol.CurrentUserEndpoint);

        private static readonly Uri RepositoriesUri =
            new Uri(GitHubProtocol.RepositoriesEndpoint);

        private readonly ILogger<GitHubService> _logger;

        private readonly GitHubOptions _options;

        private readonly ILocalizationService _localizationService;

        private readonly HttpClient _httpClient;

        public GitHubService(
            GitHubOptions options,
            ILocalizationService localizationService,
            ILogger<GitHubService> logger,
            HttpMessageHandler? handler = null)
        {
            _options = options;
            _logger = logger;
            _localizationService = localizationService;

            _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(GitHubProtocol.UserAgent);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubProtocol.ApiMediaType));
            _httpClient.DefaultRequestHeaders.Add(GitHubProtocol.ApiVersionHeader, GitHubProtocol.ApiVersion);
        }

        public async Task<GitHubDeviceAuthorization> StartDeviceAuthorizationAsync(
            CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>(GitHubProtocol.ClientIdField, _options.ClientId),
                new KeyValuePair<string, string>(GitHubProtocol.ScopeField, _options.Scope),
            ]);

            using var request = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUri)
            {
                Content = content,
            };

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GitHubProtocol.JsonMediaType));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, json);

            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;
            var expiresIn = root.GetProperty(GitHubProtocol.ExpiresInField).GetInt32();
            var interval = root.GetProperty(GitHubProtocol.IntervalField).GetInt32();

            return new GitHubDeviceAuthorization(
                root.GetProperty(GitHubProtocol.DeviceCodeField).GetString()!,
                root.GetProperty(GitHubProtocol.UserCodeField).GetString()!,
                new Uri(root.GetProperty(GitHubProtocol.VerificationUriField).GetString()!),
                DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                TimeSpan.FromSeconds(interval));
        }

        public async Task<GitHubToken> WaitForAccessTokenAsync(
            GitHubDeviceAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            var pollingInterval = authorization.PollingInterval;

            while (DateTimeOffset.UtcNow < authorization.ExpiresAtUtc)
            {
                await Task.Delay(pollingInterval, cancellationToken);

                using var content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>(GitHubProtocol.ClientIdField, _options.ClientId),
                    new KeyValuePair<string, string>(GitHubProtocol.DeviceCodeField, authorization.DeviceCode),
                    new KeyValuePair<string, string>(
                        GitHubProtocol.GrantTypeField,
                        GitHubProtocol.DeviceCodeGrantType),
                ]);

                using var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri)
                {
                    Content = content,
                };

                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(GitHubProtocol.JsonMediaType));

                using var response = await _httpClient.SendAsync(request, cancellationToken);

                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                EnsureSuccess(response, json);

                using var document = JsonDocument.Parse(json);

                var root = document.RootElement;

                if (root.TryGetProperty(GitHubProtocol.AccessTokenField, out var tokenElement))
                {
                    return ReadToken(root);
                }

                var error =
                    root.TryGetProperty(GitHubProtocol.ErrorField, out var errorElement) ?
                        errorElement.GetString() :
                        null;

                switch (error)
                {
                    case GitHubProtocol.AuthorizationPendingError:
                        continue;
                    case GitHubProtocol.SlowDownError:
                        pollingInterval += TimeSpan.FromSeconds(5);
                        continue;
                    case GitHubProtocol.AccessDeniedError:
                        throw new InvalidOperationException(Texts.GitHubAuthorizationCancelledUser);
                    case GitHubProtocol.ExpiredTokenError:
                        throw new InvalidOperationException(Texts.GitHubCodeExpiredStartConnectionAgain);
                    case GitHubProtocol.DeviceFlowDisabledError:
                        throw new InvalidOperationException(Texts.DeviceFlowNotEnabledGitHubOAuthApp);
                    case GitHubProtocol.IncorrectClientCredentialsError:
                        throw new InvalidOperationException(Texts.GitHubClientIDInvalid);
                    default:
                        throw new InvalidOperationException(
                            GetGitHubError(
                                root,
                                Texts.GitHubDidNotIssueAccessToken));
                }
            }

            throw new InvalidOperationException(Texts.GitHubCodeExpiredStartConnectionAgain);
        }

        public async Task<GitHubToken> RefreshAccessTokenAsync(
            GitHubToken token,
            CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException(Texts.GitHubSessionExpiredConnectGitHubAgain);
            }

            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>(GitHubProtocol.ClientIdField, _options.ClientId),
                new KeyValuePair<string, string>(GitHubProtocol.GrantTypeField, GitHubProtocol.RefreshTokenField),
                new KeyValuePair<string, string>(GitHubProtocol.RefreshTokenField, token.RefreshToken),
            ]);

            using var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri)
            {
                Content = content,
            };

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(GitHubProtocol.JsonMediaType));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, json);

            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;

            if (!root.TryGetProperty(GitHubProtocol.AccessTokenField, out _))
            {
                throw new InvalidOperationException(GetGitHubError(
                    root,
                    Texts.CouldNotRefreshGitHubSessionConnectGitHub));
            }

            return ReadToken(root);
        }

        public async Task<GitHubUser> GetCurrentUserAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            using var request = CreateApiRequest(CurrentUserUri, accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, json);

            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;

            return new GitHubUser(
                root.GetProperty(GitHubProtocol.IdField).GetInt64(),
                root.GetProperty(GitHubProtocol.LoginField).GetString()!,
                root.TryGetProperty(GitHubProtocol.AvatarUrlField, out var avatar) ?
                    avatar.GetString() :
                    null);
        }

        public async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            var repositories = new List<GitHubRepository>();
            Uri? pageUri = RepositoriesUri;

            while (pageUri is not null)
            {
                using var request = CreateApiRequest(pageUri, accessToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                EnsureSuccess(response, json);

                using var document = JsonDocument.Parse(json);

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    repositories.Add(new GitHubRepository(
                        item.GetProperty(GitHubProtocol.IdField).GetInt64(),
                        item.GetProperty(GitHubProtocol.NameField).GetString()!,
                        item.GetProperty(GitHubProtocol.FullNameField).GetString()!,
                        item.GetProperty(GitHubProtocol.CloneUrlField).GetString()!,
                        item.GetProperty(GitHubProtocol.DefaultBranchField).GetString()!,
                        item.GetProperty(GitHubProtocol.PrivateField).GetBoolean()));
                }

                pageUri = GetNextPageUri(response);
            }

            return repositories;
        }

        public async Task<Stream> DownloadRepositoryArchiveAsync(
            string repositoryFullName,
            string commitSha,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repositoryFullName) ||
                repositoryFullName.Split('/').Length != 2)
            {
                throw new ArgumentException(Texts.InvalidGitHubRepositoryName);
            }

            if (string.IsNullOrWhiteSpace(commitSha))
            {
                throw new ArgumentException(
                    Texts.GitCommitSHANotProvided,
                    nameof(commitSha));
            }

            var uri = new Uri($"{GitHubProtocol.RepositoryApiPrefix}{repositoryFullName}{GitHubProtocol.ArchivePathPrefix}{commitSha}");

            using var request = CreateApiRequest(uri, accessToken);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var error = response.IsSuccessStatusCode ?
                string.Empty :
                await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, error);

            var result = new MemoryStream();

            await response.Content.CopyToAsync(result, cancellationToken);

            result.Position = 0;

            return result;
        }

        public async Task<string> GetDefaultBranchCommitShaAsync(
            string repositoryFullName,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repositoryFullName) ||
                repositoryFullName.Split('/').Length != 2)
            {
                throw new ArgumentException(Texts.InvalidGitHubRepositoryName);
            }

            var uri = new Uri($"{GitHubProtocol.RepositoryApiPrefix}{repositoryFullName}{GitHubProtocol.LatestCommitQuery}");

            using var request = CreateApiRequest(uri, accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, json);

            using var document = JsonDocument.Parse(json);

            var commits = document.RootElement;

            if (commits.ValueKind != JsonValueKind.Array || commits.GetArrayLength() == 0)
            {
                throw new InvalidDataException(Texts.GitHubRepositoryNoCommits);
            }

            return commits[0].GetProperty(GitHubProtocol.ShaField).GetString() ??
                throw new InvalidDataException(Texts.GitHubDidNotReturnGitCommitSHA);
        }

        public async Task<IReadOnlyList<GitHubSubmodule>> GetSubmodulesAsync(
            string repositoryFullName,
            string commitSha,
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repositoryFullName) ||
                repositoryFullName.Split('/').Length != 2)
            {
                throw new ArgumentException(Texts.InvalidGitHubRepositoryName);
            }

            if (string.IsNullOrWhiteSpace(commitSha))
            {
                throw new ArgumentException(
                    Texts.GitCommitSHANotProvided,
                    nameof(commitSha));
            }

            var commitUri = new Uri($"{GitHubProtocol.RepositoryApiPrefix}{repositoryFullName}{GitHubProtocol.GitCommitPathPrefix}{commitSha}");

            using var commitRequest = CreateApiRequest(commitUri, accessToken);
            using var commitResponse = await _httpClient.SendAsync(
                commitRequest,
                cancellationToken);

            var commitJson = await commitResponse.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(commitResponse, commitJson);

            using var commitDocument = JsonDocument.Parse(commitJson);

            var treeSha = commitDocument.RootElement
                .GetProperty(GitHubProtocol.TreeField)
                .GetProperty(GitHubProtocol.ShaField)
                .GetString() ??
                throw new InvalidDataException(Texts.GitHubDidNotReturnRepositoryTreeSHA);

            var treeUri = new Uri($"{GitHubProtocol.RepositoryApiPrefix}{repositoryFullName}{GitHubProtocol.GitTreePathPrefix}{treeSha}{GitHubProtocol.RecursiveTreeQuery}");

            using var treeRequest = CreateApiRequest(treeUri, accessToken);
            using var treeResponse = await _httpClient.SendAsync(treeRequest, cancellationToken);

            var json = await treeResponse.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(treeResponse, json);

            using var document = JsonDocument.Parse(json);

            var result = new List<GitHubSubmodule>();

            if (!document.RootElement.TryGetProperty(GitHubProtocol.TreeField, out var tree))
            {
                return result;
            }

            foreach (var item in tree.EnumerateArray())
            {
                if (!item.TryGetProperty(GitHubProtocol.TypeField, out var type) ||
                    type.GetString() != GitHubProtocol.CommitField ||
                    !item.TryGetProperty(GitHubProtocol.PathField, out var path) ||
                    !item.TryGetProperty(GitHubProtocol.ShaField, out var sha))
                {
                    continue;
                }

                var pathValue = path.GetString();
                var shaValue = sha.GetString();
                if (!string.IsNullOrWhiteSpace(pathValue) &&
                    !string.IsNullOrWhiteSpace(shaValue))
                {
                    result.Add(new GitHubSubmodule(pathValue, shaValue));
                }
            }

            return result;
        }

        public void Dispose() =>
            _httpClient.Dispose();

        private static HttpRequestMessage CreateApiRequest(Uri uri, string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            request.Headers.Authorization = new AuthenticationHeaderValue(GitHubProtocol.BearerScheme, accessToken);

            return request;
        }

        private static Uri? GetNextPageUri(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues(GitHubProtocol.LinkHeader, out var values))
            {
                return null;
            }

            foreach (var value in values)
            {
                foreach (var part in value.Split(','))
                {
                    var sections = part.Split(';', StringSplitOptions.TrimEntries);
                    if (sections.Length < 2 || !sections[1].Contains(GitHubProtocol.NextPageRelation, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var uriText = sections[0].Trim().Trim('<', '>');

                    return Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ?
                        uri :
                        null;
                }
            }

            return null;
        }

        private void EnsureSuccess(HttpResponseMessage response, string json)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var message = _localizationService.Format(
                Texts.GitHubReturnedHTTPError,
                (int)response.StatusCode);

            try
            {
                using var document = JsonDocument.Parse(json);
                message = GetGitHubError(document.RootElement, message);
            }
            catch (JsonException exception)
            {
                _logger.LogDebug(exception, NonJsonErrorResponseMessage);
            }

            throw new HttpRequestException(message, null, response.StatusCode);
        }

        private static string GetGitHubError(JsonElement root, string fallback) =>
            root.TryGetProperty(GitHubProtocol.ErrorDescriptionField, out var description) ?
                description.GetString() ?? fallback :
                root.TryGetProperty(GitHubProtocol.MessageField, out var message) ?
                    message.GetString() ?? fallback :
                    fallback;

        private static GitHubToken ReadToken(JsonElement root)
        {
            var now = DateTimeOffset.UtcNow;

            var accessToken = root.GetProperty(GitHubProtocol.AccessTokenField).GetString()!;

            var refreshToken =
                root.TryGetProperty(GitHubProtocol.RefreshTokenField, out var refreshTokenElement) ?
                    refreshTokenElement.GetString() :
                    null;

            DateTimeOffset? accessTokenExpiresAtUtc =
                root.TryGetProperty(GitHubProtocol.ExpiresInField, out var expiresElement) &&
                expiresElement.ValueKind == JsonValueKind.Number ?
                    now.AddSeconds(expiresElement.GetInt32()) :
                    null;

            DateTimeOffset? refreshTokenExpiresAtUtc =
                root.TryGetProperty(
                    GitHubProtocol.RefreshTokenExpiresInField,
                    out var refreshExpiresElement) &&
                refreshExpiresElement.ValueKind == JsonValueKind.Number ?
                    now.AddSeconds(refreshExpiresElement.GetInt32()) :
                    null;

            return new GitHubToken(
                accessToken,
                refreshToken,
                accessTokenExpiresAtUtc,
                refreshTokenExpiresAtUtc);
        }

        private void EnsureConfigured()
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException(Texts.GitHubClientIDNotConfiguredAppsettingsJson);
            }
        }
    }
}
