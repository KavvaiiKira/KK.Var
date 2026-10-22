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
                checks.Add(checkFactory.Error(PreflightCheckKeys.Project, "Проект не найден."));
                return new DeploymentPreflightResult(checks);
            }

            checks.Add(!currentOperationOwnsQueue &&
                       operationQueue.HasActiveOperation(project.Id) ?
                checkFactory.Error(PreflightCheckKeys.Queue, "Для этого проекта уже есть активная операция.") :
                checkFactory.Success(PreflightCheckKeys.Queue, "Для проекта нет конфликтующей операции."));

            var tag = request.VersionTag?.Trim() ?? string.Empty;
            if (!VersionTagPattern.IsMatch(tag))
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Version,
                    "Тег версии может содержать только латинские буквы, цифры, точку, дефис и подчёркивание.") with
                {
                    VersionError = string.IsNullOrWhiteSpace(tag) ? VersionTagError.Required : VersionTagError.InvalidFormat,
                });
            }
            else if (await versionRepository.TagExistsAsync(project.Id, tag, cancellationToken))
            {
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.Version,
                    PreflightStatus.Error,
                    localizationService.Format("Версия «{0}» уже существует.", tag),
                    VersionError: VersionTagError.AlreadyExists));
            }
            else
            {
                checks.Add(checkFactory.Success(PreflightCheckKeys.Version, "Тег новой версии доступен."));
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
                    "Профиль машины должен быть проверен и не архивирован.",
                    exception.Message));
                return new DeploymentPreflightResult(checks);
            }
            await using var reservedMachine = machineReservation;
            var source = await sourceChecks.CheckSourceAsync(project, request, checks, cancellationToken);
            await buildChecks.CheckBuildConfigurationAsync(project, checks, cancellationToken);

            try
            {
                _ = await environmentService.GenerateAsync(project.Id, cancellationToken);
                checks.Add(checkFactory.Success(PreflightCheckKeys.Environment, "Файл переменных можно сформировать."));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.Environment,
                    "Файл переменных некорректен.",
                    exception.Message));
            }

            const long minimumFreeBytes = 64L * 1024 * 1024;
            if (source.Bytes > minimumFreeBytes)
            {
                checks.Add(checkFactory.Warning(
                    PreflightCheckKeys.SpaceEstimate,
                    "Итоговый размер сборки неизвестен; проверен минимальный запас места."));
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
