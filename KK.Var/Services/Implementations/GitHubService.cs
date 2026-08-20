using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

public sealed class GitHubService : IGitHubService, IDisposable
{
    private static readonly Uri DeviceCodeUri =
        new Uri("https://github.com/login/device/code");
    private static readonly Uri AccessTokenUri =
        new Uri("https://github.com/login/oauth/access_token");
    private static readonly Uri CurrentUserUri =
        new Uri("https://api.github.com/user");
    private static readonly Uri RepositoriesUri =
        new Uri("https://api.github.com/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member");

    private readonly GitHubOptions _options;
    private readonly ILocalizationService _localizationService;
    private readonly HttpClient _httpClient;

    public GitHubService(
        GitHubOptions options,
        ILocalizationService localizationService)
    {
        _options = options;
        _localizationService = localizationService;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KK.Var/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<GitHubDeviceAuthorization> StartDeviceAuthorizationAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("scope", _options.Scope),
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUri)
        {
            Content = content,
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var interval = root.GetProperty("interval").GetInt32();

        return new GitHubDeviceAuthorization(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            new Uri(root.GetProperty("verification_uri").GetString()!),
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
                new KeyValuePair<string, string>("client_id", _options.ClientId),
                new KeyValuePair<string, string>("device_code", authorization.DeviceCode),
                new KeyValuePair<string, string>(
                    "grant_type",
                    "urn:ietf:params:oauth:grant-type:device_code"),
            ]);

            using var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri)
            {
                Content = content,
            };

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            EnsureSuccess(response, json);

            using var document = JsonDocument.Parse(json);

            var root = document.RootElement;

            if (root.TryGetProperty("access_token", out var tokenElement))
            {
                return ReadToken(root);
            }

            var error =
                root.TryGetProperty("error", out var errorElement) ?
                    errorElement.GetString() :
                    null;

            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    pollingInterval += TimeSpan.FromSeconds(5);
                    continue;
                case "access_denied":
                    throw new InvalidOperationException(_localizationService.Get(
                        "Авторизация GitHub была отменена пользователем."));
                case "expired_token":
                    throw new InvalidOperationException(_localizationService.Get(
                        "Срок действия кода GitHub истёк. Запустите подключение ещё раз."));
                case "device_flow_disabled":
                    throw new InvalidOperationException(_localizationService.Get(
                        "Для GitHub OAuth App не включён Device Flow."));
                case "incorrect_client_credentials":
                    throw new InvalidOperationException(_localizationService.Get(
                        "GitHub Client ID указан неверно."));
                default:
                    throw new InvalidOperationException(
                        GetGitHubError(
                            root,
                            _localizationService.Get(
                                "GitHub не выдал токен доступа.")));
            }
        }

        throw new InvalidOperationException(_localizationService.Get(
            "Срок действия кода GitHub истёк. Запустите подключение ещё раз."));
    }

    public async Task<GitHubToken> RefreshAccessTokenAsync(
        GitHubToken token,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            throw new InvalidOperationException(_localizationService.Get(
                "Сеанс GitHub истёк. Подключите GitHub заново."));
        }

        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("client_id", _options.ClientId),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", token.RefreshToken),
        ]);

        using var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUri)
        {
            Content = content,
        };

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;

        if (!root.TryGetProperty("access_token", out _))
        {
            throw new InvalidOperationException(GetGitHubError(
                root,
                _localizationService.Get(
                    "Не удалось обновить сеанс GitHub. Подключите GitHub заново.")));
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
            root.GetProperty("id").GetInt64(),
            root.GetProperty("login").GetString()!,
            root.TryGetProperty("avatar_url", out var avatar) ?
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
                    item.GetProperty("id").GetInt64(),
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("full_name").GetString()!,
                    item.GetProperty("clone_url").GetString()!,
                    item.GetProperty("default_branch").GetString()!,
                    item.GetProperty("private").GetBoolean()));
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
            throw new ArgumentException(_localizationService.Get(
                "Некорректное имя репозитория GitHub."));
        }

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new ArgumentException(
                _localizationService.Get("Не указан Git commit SHA."),
                nameof(commitSha));
        }

        var uri = new Uri($"https://api.github.com/repos/{repositoryFullName}/zipball/{commitSha}");

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
            throw new ArgumentException(_localizationService.Get(
                "Некорректное имя репозитория GitHub."));
        }

        var uri = new Uri($"https://api.github.com/repos/{repositoryFullName}/commits?per_page=1");

        using var request = CreateApiRequest(uri, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(response, json);

        using var document = JsonDocument.Parse(json);

        var commits = document.RootElement;

        if (commits.ValueKind != JsonValueKind.Array || commits.GetArrayLength() == 0)
        {
            throw new InvalidDataException(_localizationService.Get(
                "В репозитории GitHub нет коммитов."));
        }

        return commits[0].GetProperty("sha").GetString() ??
            throw new InvalidDataException(_localizationService.Get(
                "GitHub не вернул Git commit SHA."));
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
            throw new ArgumentException(_localizationService.Get(
                "Некорректное имя репозитория GitHub."));
        }

        if (string.IsNullOrWhiteSpace(commitSha))
        {
            throw new ArgumentException(
                _localizationService.Get("Не указан Git commit SHA."),
                nameof(commitSha));
        }

        var commitUri = new Uri($"https://api.github.com/repos/{repositoryFullName}/git/commits/{commitSha}");

        using var commitRequest = CreateApiRequest(commitUri, accessToken);
        using var commitResponse = await _httpClient.SendAsync(
            commitRequest,
            cancellationToken);

        var commitJson = await commitResponse.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(commitResponse, commitJson);

        using var commitDocument = JsonDocument.Parse(commitJson);

        var treeSha = commitDocument.RootElement
            .GetProperty("tree")
            .GetProperty("sha")
            .GetString() ??
            throw new InvalidDataException(_localizationService.Get(
                "GitHub не вернул SHA дерева репозитория."));

        var treeUri = new Uri($"https://api.github.com/repos/{repositoryFullName}/git/trees/{treeSha}?recursive=1");

        using var treeRequest = CreateApiRequest(treeUri, accessToken);
        using var treeResponse = await _httpClient.SendAsync(treeRequest, cancellationToken);

        var json = await treeResponse.Content.ReadAsStringAsync(cancellationToken);

        EnsureSuccess(treeResponse, json);

        using var document = JsonDocument.Parse(json);

        var result = new List<GitHubSubmodule>();

        if (!document.RootElement.TryGetProperty("tree", out var tree))
        {
            return result;
        }

        foreach (var item in tree.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) ||
                type.GetString() != "commit" ||
                !item.TryGetProperty("path", out var path) ||
                !item.TryGetProperty("sha", out var sha))
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static HttpRequestMessage CreateApiRequest(Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return request;
    }

    private static Uri? GetNextPageUri(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            foreach (var part in value.Split(','))
            {
                var sections = part.Split(';', StringSplitOptions.TrimEntries);
                if (sections.Length < 2 || !sections[1].Contains("rel=\"next\"", StringComparison.Ordinal))
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
            "GitHub вернул ошибку HTTP {0}.",
            (int)response.StatusCode);

        try
        {
            using var document = JsonDocument.Parse(json);
            message = GetGitHubError(document.RootElement, message);
        }
        catch (JsonException)
        {
        }

        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string GetGitHubError(JsonElement root, string fallback) =>
        root.TryGetProperty("error_description", out var description) ?
            description.GetString() ?? fallback :
            root.TryGetProperty("message", out var message) ?
                message.GetString() ?? fallback :
                fallback;

    private static GitHubToken ReadToken(JsonElement root)
    {
        var now = DateTimeOffset.UtcNow;

        var accessToken = root.GetProperty("access_token").GetString()!;

        var refreshToken =
            root.TryGetProperty("refresh_token", out var refreshTokenElement) ?
                refreshTokenElement.GetString() :
                null;

        DateTimeOffset? accessTokenExpiresAtUtc =
            root.TryGetProperty("expires_in", out var expiresElement) &&
            expiresElement.ValueKind == JsonValueKind.Number ?
                now.AddSeconds(expiresElement.GetInt32()) :
                null;

        DateTimeOffset? refreshTokenExpiresAtUtc =
            root.TryGetProperty(
                "refresh_token_expires_in",
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
            throw new InvalidOperationException(_localizationService.Get(
                "GitHub Client ID не настроен в appsettings.json."));
        }
    }
}
