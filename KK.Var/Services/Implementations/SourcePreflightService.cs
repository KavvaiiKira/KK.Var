using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class SourcePreflightService(
        IGitHubService gitHubService,
        IGitHubAuthenticationService gitHubAuthenticationService,
        IPreflightCheckFactory checkFactory,
        ILocalizationService localizationService) : ISourcePreflightService
    {
        public async Task<(long Bytes, string? CommitSha)> CheckSourceAsync(
            KKProject project,
            DeploymentRequest request,
            List<DeploymentPreflightCheck> checks,
            CancellationToken cancellationToken)
        {
            if (project.SourceType == ProjectSourceType.LocalDirectory)
            {
                try
                {
                    var path = project.LocalDirectoryPath;
                    if (string.IsNullOrWhiteSpace(path) ||
                        !Path.IsPathFullyQualified(path) ||
                        !Directory.Exists(path))
                    {
                        throw new DirectoryNotFoundException(path);
                    }

                    var bytes = await Task.Run(
                        () => EstimateSourceBytes(path, cancellationToken),
                        cancellationToken);

                    checks.Add(checkFactory.Success(PreflightCheckKeys.Source, "Локальный исходный каталог доступен."));
                    return (bytes, null);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    checks.Add(checkFactory.Error(
                        PreflightCheckKeys.Source,
                        "Локальный исходный каталог недоступен.",
                        exception.Message));
                    return (0, null);
                }
            }

            if (project.SourceType == ProjectSourceType.GitHubRepository)
            {
                try
                {
                    var token = await gitHubAuthenticationService.GetTokenAsync(cancellationToken) ??
                        throw new InvalidOperationException(localizationService.Get(
                            "Подключите GitHub в настройках перед сборкой проекта."));
                    var commit = request.SourceCommitSha is not null ?
                        await gitHubService.GetCommitAsync(
                            project.GitHubRepositoryFullName ?? string.Empty,
                            request.SourceCommitSha,
                            token.AccessToken,
                            cancellationToken) :
                        await gitHubService.GetBranchCommitAsync(
                            project.GitHubRepositoryFullName ?? string.Empty,
                            request.GitHubBranch ?? project.GitHubBranch,
                            token.AccessToken,
                            cancellationToken);
                    var commitSha = commit.Sha;
                    checks.Add(checkFactory.Success(PreflightCheckKeys.Source, "GitHub-репозиторий доступен.", commitSha));
                    checks.Add(checkFactory.Warning(
                        PreflightCheckKeys.SourceSize,
                        "Размер GitHub-архива станет известен после загрузки."));
                    return (0, commitSha);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    checks.Add(checkFactory.Error(
                        PreflightCheckKeys.Source,
                        "GitHub-репозиторий недоступен.",
                        exception.Message));
                }

                return (0, null);
            }

            checks.Add(checkFactory.Error(PreflightCheckKeys.Source, "Тип источника проекта недопустим."));
            return (0, null);
        }

        private static long EstimateSourceBytes(
            string root,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            var bytes = 0L;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pending.Pop();

                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        bytes = checked(bytes + info.Length);
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var info = new DirectoryInfo(child);
                    if (!SourceFiles.IsIgnoredDirectory(info.Name) &&
                        (info.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
            }

            return bytes;
        }
    }
}
