using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

public sealed partial class GitHubService
{
    private string RepositoryPath(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(_localizationService.Get("Некорректное имя репозитория GitHub."));
        }
        return string.Join('/', parts.Select(Uri.EscapeDataString));
    }

    private async Task<JsonDocument> ReadRepositoryApiAsync(
        string repository,
        string suffix,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var uri = new Uri($"https://api.github.com/repos/{RepositoryPath(repository)}{suffix}");
        using var request = CreateApiRequest(uri, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json);
        return JsonDocument.Parse(json);
    }

    public async Task<IReadOnlyList<string>> GetBranchesAsync(
        string repositoryFullName,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var branches = new List<string>();
        for (var page = 1; ; page++)
        {
            using var document = await ReadRepositoryApiAsync(repositoryFullName,
                $"/branches?per_page=100&page={page}", accessToken, cancellationToken);
            foreach (var branch in document.RootElement.EnumerateArray())
            {
                branches.Add(branch.GetProperty("name").GetString()!);
            }
            if (document.RootElement.GetArrayLength() < 100)
            {
                return branches;
            }
        }
    }

    public async Task<GitHubCommit> GetBranchCommitAsync(
        string repositoryFullName,
        string? branch,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            using var repository = await ReadRepositoryApiAsync(
                repositoryFullName, string.Empty, accessToken, cancellationToken);
            branch = repository.RootElement.GetProperty("default_branch").GetString();
        }
        using var document = await ReadRepositoryApiAsync(repositoryFullName,
            "/branches/" + Uri.EscapeDataString(branch ?? string.Empty), accessToken, cancellationToken);
        return ReadCommit(document.RootElement.GetProperty("commit"));
    }

    public async Task<GitHubCommit> GetCommitAsync(
        string repositoryFullName,
        string commitSha,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (commitSha.Length != 40 || !commitSha.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(_localizationService.Get("Некорректный Git commit SHA."));
        }
        using var document = await ReadRepositoryApiAsync(repositoryFullName,
            "/commits/" + commitSha, accessToken, cancellationToken);
        return ReadCommit(document.RootElement);
    }

    private GitHubCommit ReadCommit(JsonElement element)
    {
        var sha = element.GetProperty("sha").GetString() ?? string.Empty;
        if (sha.Length != 40 || !sha.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(_localizationService.Get("Некорректный Git commit SHA."));
        }
        var commit = element.GetProperty("commit");
        return new GitHubCommit(sha,
            commit.GetProperty("author").GetProperty("name").GetString() ?? string.Empty,
            commit.GetProperty("message").GetString() ?? string.Empty);
    }
}
