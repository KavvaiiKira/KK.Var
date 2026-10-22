using System;
using System.Globalization;
using KK.Var.Enums;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        private string Validate()
        {
            if (SelectedProjectMachine is null || !SelectedProjectMachine.IsVerified || SelectedProjectMachine.IsArchived)
            {
                return Texts.SelectVerifiedMachineProfile;
            }
            if (IsProjectMachineChanged && !MachineSwitchConfirmed)
            {
                return Texts.ConfirmProjectSMachineChange;
            }
            if (!string.IsNullOrEmpty(BuildEditor.Error))
            {
                return BuildEditor.Error;
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                return Texts.EnterProjectName;
            }

            if (IsLocalSource && string.IsNullOrWhiteSpace(LocalDirectoryPath))
            {
                return Texts.SelectLocalProjectFolder;
            }

            if (IsGitHubSource && SelectedGitHubRepository is null)
            {
                return Texts.SelectGitHubRepository;
            }

            if (string.IsNullOrWhiteSpace(RemoteServiceName))
            {
                return Texts.EnterSystemdServiceName;
            }

            if (string.IsNullOrWhiteSpace(RemoteExecutableFileName))
            {
                return Texts.EnterExecutableFileEntryPoint;
            }

            if (string.IsNullOrWhiteSpace(RemoteDeploymentDirectory))
            {
                return Texts.EnterDeploymentDirectoryRemoteMachine;
            }

            if (string.IsNullOrWhiteSpace(ProjectEnvironmentFilePath))
            {
                return Texts.EnterEnvironmentFilePathInsideProject;
            }

            if (MaxStoredVersions is < 1 or > 10000)
            {
                return Texts.MaximumVersionCountMustBetween110000;
            }

            if (!TryParseStorageLimitBytes(out _))
            {
                return Texts.VersionSizeLimitMustPositiveNumberMB;
            }

            var healthCheckError = ValidateHealthCheck();
            if (!string.IsNullOrEmpty(healthCheckError))
            {
                return healthCheckError;
            }

            try
            {
                var provider = SelectedBuildProvider;
                var configuration = _buildConfigurationService.ReadAndValidate(
                    BuildConfigurationJson,
                    provider,
                    IsLocalSource ? LocalDirectoryPath : null);
                if (provider == ProjectBuildProvider.Custom &&
                    string.IsNullOrWhiteSpace(configuration.Command))
                {
                    return Texts.EnterCommandCustomBuild;
                }

                if (provider == ProjectBuildProvider.Cpp &&
                    string.IsNullOrWhiteSpace(configuration.ToolchainFile))
                {
                    return Texts.EnterToolchainFileCLinuxBuild;
                }
            }
            catch (System.IO.IOException exception)
            {
                return Localize(exception.Message);
            }
            catch (ArgumentException)
            {
                return Texts.CheckPathsBuildSettings;
            }
            catch (System.Text.Json.JsonException)
            {
                return Texts.BuildParametersContainInvalidJSON;
            }

            return string.Empty;
        }

        private long? ParseStorageLimitBytes()
        {
            _ = TryParseStorageLimitBytes(out var bytes);
            return bytes;
        }

        private bool TryParseStorageLimitBytes(out long? bytes)
        {
            bytes = null;

            if (string.IsNullOrWhiteSpace(MaxStoredVersionSizeMb))
            {
                return true;
            }

            if (!decimal.TryParse(
                MaxStoredVersionSizeMb,
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var megabytes) ||
                megabytes <= 0 ||
                megabytes > long.MaxValue / 1_048_576m)
            {
                return false;
            }

            bytes = decimal.ToInt64(decimal.Ceiling(megabytes * 1_048_576m));
            return true;
        }
    }
}
