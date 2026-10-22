using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using static KK.Var.Services.SourceFiles;

namespace KK.Var.Services.Implementations
{
    public sealed class ProjectSourceService(
        IGitHubService gitHubService,
        IGitHubAuthenticationService gitHubAuthenticationService,
        ILocalizationService localizationService) : IProjectSourceService
    {
        private const string ParentDirectoryPrefix = "../";

        private const string GitHubHttpsPrefix = "https://github.com/";

        private const string GitHubSshPrefix = "git@github.com:";

        private const string GitModulesFileName = ".gitmodules";

        private const string GitModulePathKey = "path";

        private const string GitModuleUrlKey = "url";

        private const string GitModuleEndSection = "[end]";

        private const string RepositoryExtension = ".git";

        public async Task<string?> AcquireSourceAsync(
            KKProject project,
            string destination,
            string? sourceCommitSha,
            CancellationToken cancellationToken)
        {
            if (project.SourceType == ProjectSourceType.LocalDirectory)
            {
                var source = project.LocalDirectoryPath ??
                    throw new InvalidOperationException(localizationService.Get(
                        "Не указана локальная папка проекта."));

                if (!Directory.Exists(source))
                {
                    throw new DirectoryNotFoundException(localizationService.Format(
                        "Локальная папка проекта не найдена: {0}",
                        source));
                }

                await Task.Run(
                    () => CopyDirectory(source, destination, cancellationToken),
                    cancellationToken);

                return null;
            }

            var repository = project.GitHubRepositoryFullName ??
                throw new InvalidOperationException(localizationService.Get(
                    "Не указан репозиторий GitHub."));

            var token = await gitHubAuthenticationService.GetTokenAsync(cancellationToken);
            if (token is null)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Подключите GitHub в настройках перед сборкой проекта."));
            }

            var commit = sourceCommitSha is not null ?
                await gitHubService.GetCommitAsync(
                    repository,
                    sourceCommitSha,
                    token.AccessToken,
                    cancellationToken) :
                await gitHubService.GetBranchCommitAsync(
                    repository,
                    project.GitHubBranch,
                    token.AccessToken,
                    cancellationToken);
            var commitSha = commit.Sha;

            await using var archive = await gitHubService.DownloadRepositoryArchiveAsync(
                repository,
                commitSha,
                token.AccessToken,
                cancellationToken);

            using var zip = new ZipArchive(archive, ZipArchiveMode.Read);

            ExtractGitHubArchive(zip, destination);

            await PopulateGitHubSubmodulesAsync(
                repository,
                commitSha,
                destination,
                token.AccessToken,
                0,
                cancellationToken);

            return commitSha;
        }

        private async Task PopulateGitHubSubmodulesAsync(
            string repositoryFullName,
            string commitSha,
            string sourceDirectory,
            string accessToken,
            int depth,
            CancellationToken cancellationToken)
        {
            if (depth >= 8)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Слишком большая вложенность Git submodule."));
            }

            var submodules = await gitHubService.GetSubmodulesAsync(
                repositoryFullName,
                commitSha,
                accessToken,
                cancellationToken);

            if (submodules.Count == 0)
            {
                return;
            }

            var configurations = ReadGitModules(sourceDirectory);

            foreach (var submodule in submodules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedPath = submodule.Path.Replace('\\', '/').Trim('/');

                if (!configurations.TryGetValue(normalizedPath, out var url))
                {
                    throw new InvalidDataException(localizationService.Format(
                        "Для Git submodule «{0}» не найден URL в .gitmodules.",
                        normalizedPath));
                }

                var submoduleRepository = ResolveGitHubRepository(repositoryFullName, url);
                var targetDirectory = ResolveSubmoduleDirectory(sourceDirectory, normalizedPath);

                Directory.CreateDirectory(targetDirectory);

                await using var archive = await gitHubService.DownloadRepositoryArchiveAsync(
                    submoduleRepository,
                    submodule.CommitSha,
                    accessToken,
                    cancellationToken);

                using var zip = new ZipArchive(archive, ZipArchiveMode.Read);

                ExtractGitHubArchive(zip, targetDirectory);

                await PopulateGitHubSubmodulesAsync(
                    submoduleRepository,
                    submodule.CommitSha,
                    targetDirectory,
                    accessToken,
                    depth + 1,
                    cancellationToken);
            }
        }

        private static IReadOnlyDictionary<string, string> ReadGitModules(string sourceDirectory)
        {
            var path = Path.Combine(sourceDirectory, GitModulesFileName);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!File.Exists(path))
            {
                return result;
            }

            string? currentPath = null;
            string? currentUrl = null;

            foreach (var rawLine in File.ReadLines(path).Append(GitModuleEndSection))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrWhiteSpace(currentPath) &&
                        !string.IsNullOrWhiteSpace(currentUrl))
                    {
                        result[currentPath.Replace('\\', '/').Trim('/')] = currentUrl;
                    }

                    currentPath = null;
                    currentUrl = null;

                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();

                if (key.Equals(GitModulePathKey, StringComparison.OrdinalIgnoreCase))
                {
                    currentPath = value;
                }
                else if (key.Equals(GitModuleUrlKey, StringComparison.OrdinalIgnoreCase))
                {
                    currentUrl = value;
                }
            }

            return result;
        }

        private string ResolveGitHubRepository(string parentRepository, string url)
        {
            var value = url.Trim();
            if (value.StartsWith(ParentDirectoryPrefix, StringComparison.Ordinal))
            {
                var owner = parentRepository.Split('/')[0];
                var repositoryName = value[3..].TrimEnd('/');

                if (repositoryName.EndsWith(RepositoryExtension, StringComparison.OrdinalIgnoreCase))
                {
                    repositoryName = repositoryName[..^4];
                }

                return $"{owner}/{repositoryName}";
            }

            const string httpsPrefix = GitHubHttpsPrefix;
            const string sshPrefix = GitHubSshPrefix;

            if (value.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[httpsPrefix.Length..];
            }
            else if (value.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[sshPrefix.Length..];
            }
            else
            {
                throw new InvalidDataException(localizationService.Format(
                    "Git submodule использует неподдерживаемый URL: {0}",
                    url));
            }

            value = value.Trim('/');

            if (value.EndsWith(RepositoryExtension, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^4];
            }

            if (value.Split('/').Length != 2)
            {
                throw new InvalidDataException(localizationService.Format(
                    "Некорректный GitHub URL для submodule: {0}",
                    url));
            }

            return value;
        }

        private string ResolveSubmoduleDirectory(string root, string relativePath)
        {
            var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

            return !target.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ?
                throw new InvalidDataException(localizationService.Get("Путь к подмодулю Git выходит за пределы репозитория.")) :
                target;
        }

        private void ExtractGitHubArchive(ZipArchive zip, string destination)
        {
            var prefix = zip.Entries
                .Select(entry => entry.FullName.Split('/')[0])
                .FirstOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ??
                    throw new InvalidDataException(localizationService.Get("Архив GitHub пуст."));

            var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

            foreach (var entry in zip.Entries)
            {
                var relative =
                    entry.FullName.StartsWith(prefix + "/", StringComparison.Ordinal) ?
                        entry.FullName[(prefix.Length + 1)..] :
                        entry.FullName;

                if (string.IsNullOrEmpty(relative))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(destination, relative));

                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(localizationService.Get(
                        "Архив GitHub содержит небезопасный путь."));
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
        }
    }
}
