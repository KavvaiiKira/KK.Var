using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class DeploymentPreflightService(
        IKKProjectRepository projectRepository,
        IKKProjectVersionRepository versionRepository,
        IKKProjectEnvironmentService environmentService,
        ISourcePreflightService sourceChecks,
        IBuildPreflightService buildChecks,
        IStoragePreflightService storageChecks,
        IRemoteMachineService machineService,
        IDeploymentOperationQueue operationQueue,
        IRemoteConnectionService remoteConnectionService,
        IPreflightCheckFactory checkFactory,
        ILocalizationService localizationService) : IDeploymentPreflightService
    {
        private static readonly Regex VersionTagPattern = new Regex(
            ArtifactConstants.VersionTagPattern,
            RegexOptions.CultureInvariant);

        public async Task<DeploymentPreflightResult> CheckAsync(
            DeploymentRequest request,
            bool currentOperationOwnsQueue = false,
            CancellationToken cancellationToken = default,
            KKProject? operationProject = null)
        {
            var checks = new List<DeploymentPreflightCheck>();
            var project = operationProject ?? await projectRepository.GetByIdAsync(
                request.ProjectId,
                cancellationToken);

            if (project is null)
            {
                checks.Add(checkFactory.Error(PreflightCheckKeys.Project, Texts.ProjectNotFound));
                return new DeploymentPreflightResult(checks);
            }

            checks.Add(!currentOperationOwnsQueue &&
                       operationQueue.HasActiveOperation(project.Id) ?
                checkFactory.Error(PreflightCheckKeys.Queue, Texts.ProjectAlreadyActiveOperation) :
                checkFactory.Success(PreflightCheckKeys.Queue, Texts.NoConflictingOperationProject));

            var tag = request.VersionTag?.Trim() ?? string.Empty;
            if (!VersionTagPattern.IsMatch(tag))
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Version,
                    Texts.VersionTagMayContainOnlyLatinLetters) with
                {
                    VersionError = string.IsNullOrWhiteSpace(tag) ? VersionTagError.Required : VersionTagError.InvalidFormat,
                });
            }
            else if (await versionRepository.TagExistsAsync(project.Id, tag, cancellationToken))
            {
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Version,
                    PreflightStatus.Error,
                    localizationService.Format(Texts.VersionAlreadyExists, tag),
                    VersionError: VersionTagError.AlreadyExists));
            }
            else
            {
                checks.Add(checkFactory.Success(PreflightCheckKeys.Version, Texts.NewVersionTagAvailable));
            }

            RemoteMachineSettings connection;
            IAsyncDisposable? machineReservation = null;
            try
            {
                machineReservation = await machineService.ReserveAsync(
                    request.RemoteMachineId ?? project.RemoteMachineId,
                    cancellationToken);
                connection = await machineService.GetConnectionAsync(
                    request.RemoteMachineId ?? project.RemoteMachineId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                if (machineReservation is not null)
                {
                    await machineReservation.DisposeAsync();
                }
                if (exception is OperationCanceledException)
                {
                    throw;
                }
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Machine,
                    Texts.MachineProfileMustVerifiedNotArchived,
                    exception.Message));
                return new DeploymentPreflightResult(checks);
            }
            await using var reservedMachine = machineReservation;
            var source = await sourceChecks.CheckSourceAsync(project, request, checks, cancellationToken);
            await buildChecks.CheckBuildConfigurationAsync(project, checks, cancellationToken);

            try
            {
                _ = await environmentService.GenerateAsync(project.Id, cancellationToken);
                checks.Add(checkFactory.Success(PreflightCheckKeys.Environment, Texts.EnvironmentFileCanGenerated));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Environment,
                    Texts.EnvironmentFileInvalid,
                    exception.Message));
            }

            const long minimumFreeBytes = 64L * 1024 * 1024;
            if (source.Bytes > minimumFreeBytes)
            {
                checks.Add(checkFactory.Warning(
                    PreflightCheckKeys.SpaceEstimate,
                    Texts.FinalBuildSizeUnknownMinimumFreeSpace));
            }

            storageChecks.CheckRemotePath(project, checks);
            await storageChecks.CheckLocalStorageAsync(checks, minimumFreeBytes, cancellationToken);

            if (checks.Any(check =>
                    check.Key == PreflightCheckKeys.RemotePath && check.Status == PreflightStatus.Error))
            {
                return new DeploymentPreflightResult(checks, source.CommitSha);
            }

            var remoteChecks = await remoteConnectionService.CheckDeploymentAsync(
                connection,
                project.RemoteDeploymentDirectory,
                minimumFreeBytes,
                project.BuildProvider == ProjectBuildProvider.Python ||
                                project.RemoteExecutableFileName.EndsWith(BuildConstants.PythonScriptExtension, StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            checks.AddRange(remoteChecks);

            return new DeploymentPreflightResult(checks, source.CommitSha);
        }
    }
}
