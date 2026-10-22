using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class StoragePreflightService(
        IArtifactStorageService artifactStorageService,
        IPreflightCheckFactory checkFactory,
        ILocalizationService localizationService) : IStoragePreflightService
    {
        public async Task CheckLocalStorageAsync(
            List<DeploymentPreflightCheck> checks,
            long minimumFreeBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                var root = await artifactStorageService.GetEffectiveRootAsync(cancellationToken);
                Directory.CreateDirectory(root);
                var probe = Path.Combine(root, $".kk-var-preflight-{Guid.NewGuid():N}.tmp");
                try
                {
                    await using var stream = new FileStream(
                        probe,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None);
                    await stream.WriteAsync(new byte[] { 0 }, cancellationToken);
                }
                finally
                {
                    if (File.Exists(probe))
                    {
                        File.Delete(probe);
                    }
                }

                var drive = new DriveInfo(Path.GetPathRoot(root) ?? root);
                checks.Add(new DeploymentPreflightCheck(
                    PreflightCheckKeys.LocalSpace,
                    drive.AvailableFreeSpace >= minimumFreeBytes ?
                        PreflightStatus.Success :
                        PreflightStatus.Error,
                    localizationService.Get(drive.AvailableFreeSpace >= minimumFreeBytes ?
                        "Локальный архив можно создать; свободное место проверено." :
                        "Недостаточно места для локального архива."),
                    localizationService.Format(
                        "Доступно байт: {0}; требуется минимум: {1}.",
                        drive.AvailableFreeSpace,
                        minimumFreeBytes)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(checkFactory.Error(
                    PreflightCheckKeys.LocalSpace,
                    "Не удалось подготовить локальный архив.",
                    exception.Message));
            }
        }

        public void CheckRemotePath(
            KKProject project,
            List<DeploymentPreflightCheck> checks)
        {
            var path = project.RemoteDeploymentDirectory;
            var segments = path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var valid = !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith("/", StringComparison.Ordinal) &&
                path.IndexOfAny(['\0', '\r', '\n']) < 0 &&
                segments.Length >= 2 &&
                !(segments[0].Equals(RemotePaths.HomeDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                  segments.Length < 3) &&
                !segments.Any(segment => segment is "." or "..") &&
                !RemotePaths.IsProtectedDirectory(path) &&
                IsSafeRelativeFilePath(project.ProjectEnvironmentFilePath) &&
                IsSafeRelativeFilePath(project.RemoteExecutableFileName);

            checks.Add(valid ?
                checkFactory.Success(PreflightCheckKeys.RemotePath, "Удалённый путь безопасен для Deploy.") :
                checkFactory.Error(PreflightCheckKeys.RemotePath, "Удалённый путь Deploy небезопасен."));
        }

        private static bool IsSafeRelativeFilePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                Path.IsPathRooted(value) ||
                value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            {
                return false;
            }

            return value.Replace('\\', '/').Split('/')
                .All(segment => !string.IsNullOrWhiteSpace(segment) &&
                    segment is not ("." or ".."));
        }
    }
}
