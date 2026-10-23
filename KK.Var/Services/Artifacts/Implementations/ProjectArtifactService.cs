using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class ProjectArtifactService(
        IBuildConfigurationService buildConfigurationService,
        IKKProjectEnvironmentService environmentService,
        IProjectSourceService sourceService,
        IProjectBuildService buildService,
        IArtifactPackagingService packagingService,
        IArtifactStorageService artifactStorageService,
        ILocalizationService localizationService,
        Func<Guid>? temporaryArchiveIdFactory = null) : IProjectArtifactService
    {
        private const string DotNetProviderName = ".NET";

        private const string PythonProviderName = "Python";

        private const string GoProviderName = "Go";

        private const string CppProviderName = "C++";

        private const string SourceDirectoryName = "source";

        private const string OutputDirectoryName = "output";

        private static readonly Regex TagPattern = new Regex(
            ArtifactConstants.VersionTagPattern,
            RegexOptions.CultureInvariant);

        public Task DeleteAllAsync(
            Guid projectId,
            CancellationToken cancellationToken = default,
            IArtifactStorageDestructiveLease? reservation = null) =>
            artifactStorageService.DeleteProjectArtifactsAsync(projectId, cancellationToken, reservation);

        public async Task<ProjectArtifact> CreateAsync(
            KKProject project,
            string versionTag,
            string remoteArchitecture,
            IProgress<DeploymentProgress>? progress = null,
            string? sourceCommitSha = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(project);

            var tag = versionTag.Trim();
            if (!TagPattern.IsMatch(tag))
            {
                throw new VersionTagException(VersionTagError.InvalidFormat, Texts.VersionTagMayContainOnlyLatinLetters);
            }

            var operationDirectory = Path.Combine(
                Path.GetTempPath(),
                ApplicationConstants.Name,
                Guid.NewGuid().ToString("N"));

            var sourceDirectory = Path.Combine(operationDirectory, SourceDirectoryName);
            var outputDirectory = Path.Combine(operationDirectory, OutputDirectoryName);

            string? temporaryArchivePath = null;
            var ownsTemporaryArchive = false;

            Directory.CreateDirectory(sourceDirectory);
            Directory.CreateDirectory(outputDirectory);

            try
            {
                progress?.Report(new DeploymentProgress(
                    5,
                    Texts.PreparingSourceCode));
                sourceCommitSha = await sourceService.AcquireSourceAsync(
                    project,
                    sourceDirectory,
                    sourceCommitSha,
                    cancellationToken);

                var configuration = buildConfigurationService.Read(project.BuildConfigurationJson);

                var buildSourceDirectory = ResolveBuildSourceDirectory(
                    configuration.WorkingDirectory,
                    sourceDirectory);

                var provider = DetectProvider(project, buildSourceDirectory);
                buildConfigurationService.Validate(configuration, provider, sourceDirectory);

                progress?.Report(new DeploymentProgress(
                    15,
                    localizationService.Format(
                        Texts.Build2,
                        FormatProvider(provider))));

                await buildService.BuildAsync(
                    provider,
                    project,
                    configuration,
                    buildSourceDirectory,
                    outputDirectory,
                    remoteArchitecture,
                    cancellationToken);

                await environmentService.WriteFileAsync(
                    project.Id,
                    outputDirectory,
                    cancellationToken);

                var executablePath = Path.GetFullPath(Path.Combine(
                    outputDirectory,
                    GetBuiltExecutableRelativePath(project, provider)
                                        .Replace('/', Path.DirectorySeparatorChar)));

                if (!File.Exists(executablePath))
                {
                    throw new InvalidOperationException(localizationService.Format(
                        Texts.ExecutableFileNotFoundBuild,
                        project.RemoteExecutableFileName));
                }

                progress?.Report(new DeploymentProgress(
                    45,
                    Texts.CreatingLocalArchive));

                var artifactRoot = await artifactStorageService.GetEffectiveRootAsync(
                    cancellationToken);
                var projectDirectory = Path.Combine(
                    artifactRoot,
                    project.Id.ToString("N"));

                Directory.CreateDirectory(projectDirectory);

                var relativePath = Path.Combine(
                    project.Id.ToString("N"),
                    $"{tag}{ArtifactConstants.ArchiveExtension}");

                var artifactPath = await artifactStorageService.ResolvePathAsync(
                    relativePath,
                    cancellationToken);

                if (File.Exists(artifactPath))
                {
                    throw new VersionTagException(VersionTagError.AlreadyExists, localizationService.Format(
                        Texts.ArtifactVersionAlreadyExists,
                        tag));
                }

                temporaryArchivePath = Path.Combine(
                    Path.GetDirectoryName(artifactPath)!,
                    $".{(temporaryArchiveIdFactory?.Invoke() ?? Guid.NewGuid()):N}{ArtifactConstants.ArchiveExtension}");

                await using (var file = new FileStream(
                    temporaryArchivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous))
                {
                    ownsTemporaryArchive = true;
                    await packagingService.CreateTarGzAsync(outputDirectory, file, cancellationToken);
                }

                var hash = await packagingService.CalculateSha256Async(temporaryArchivePath, cancellationToken);
                var size = new FileInfo(temporaryArchivePath).Length;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    File.Move(temporaryArchivePath, artifactPath, overwrite: false);
                }
                catch (IOException exception) when (File.Exists(artifactPath))
                {
                    throw new VersionTagException(
                        VersionTagError.AlreadyExists,
                        localizationService.Format(Texts.ArtifactVersionAlreadyExists, tag),
                        exception);
                }

                return new ProjectArtifact(
                    artifactPath,
                    relativePath.Replace('\\', '/'),
                    hash,
                    size,
                    sourceCommitSha,
                    provider);
            }
            catch (Exception exception)
            {
                if (exception is InvalidDataException or DirectoryNotFoundException)
                {
                    throw new InvalidOperationException(localizationService.Get(exception.Message), exception);
                }

                throw;
            }
            finally
            {
                if (ownsTemporaryArchive && temporaryArchivePath is not null && File.Exists(temporaryArchivePath))
                {
                    File.Delete(temporaryArchivePath);
                }

                if (Directory.Exists(operationDirectory))
                {
                    Directory.Delete(operationDirectory, recursive: true);
                }
            }
        }

        private ProjectBuildProvider DetectProvider(KKProject project, string sourceDirectory) =>
            project.BuildProvider != ProjectBuildProvider.Unknown ?
                project.BuildProvider :
                buildConfigurationService.DetectProvider(sourceDirectory);

        private string ResolveBuildSourceDirectory(string? configuredDirectory, string sourceDirectory)
        {
            var path = buildConfigurationService.ResolveSourcePath(
                sourceDirectory,
                string.IsNullOrWhiteSpace(configuredDirectory) ? "." : configuredDirectory);
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(Texts.BuildWorkingDirectoryNotFound);
            }
            return path;
        }

        private static string GetBuiltExecutableRelativePath(
            KKProject project,
            ProjectBuildProvider provider) =>
            provider == ProjectBuildProvider.DotNet &&
            project.RemoteExecutableFileName.EndsWith(BuildConstants.DotNetAssemblyExtension, StringComparison.OrdinalIgnoreCase) ?
                project.RemoteExecutableFileName[..^4] :
                project.RemoteExecutableFileName;

        private string FormatProvider(ProjectBuildProvider provider) => provider switch
        {
            ProjectBuildProvider.DotNet => DotNetProviderName,
            ProjectBuildProvider.Python => PythonProviderName,
            ProjectBuildProvider.Go => GoProviderName,
            ProjectBuildProvider.Cpp => CppProviderName,
            ProjectBuildProvider.Custom => Texts.CustomScript,
            _ => provider.ToString(),
        };
    }
}
