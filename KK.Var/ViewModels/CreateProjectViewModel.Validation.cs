using System;
using System.Globalization;
using KK.Var.Enums;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        private string Validate()
        {
            if (SelectedProjectMachine is null || !SelectedProjectMachine.IsVerified || SelectedProjectMachine.IsArchived)
            {
                return Localize("Выберите проверенный профиль машины.");
            }
            if (IsProjectMachineChanged && !MachineSwitchConfirmed)
            {
                return Localize("Подтвердите смену машины проекта.");
            }
            if (!string.IsNullOrEmpty(BuildEditor.Error))
            {
                return BuildEditor.Error;
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                return Localize("Укажите название проекта.");
            }

            if (IsLocalSource && string.IsNullOrWhiteSpace(LocalDirectoryPath))
            {
                return Localize("Выберите локальную папку проекта.");
            }

            if (IsGitHubSource && SelectedGitHubRepository is null)
            {
                return Localize("Выберите репозиторий GitHub.");
            }

            if (string.IsNullOrWhiteSpace(RemoteServiceName))
            {
                return Localize("Укажите название systemd-сервиса.");
            }

            if (string.IsNullOrWhiteSpace(RemoteExecutableFileName))
            {
                return Localize("Укажите исполняемый файл или точку входа.");
            }

            if (string.IsNullOrWhiteSpace(RemoteDeploymentDirectory))
            {
                return Localize(
                    "Укажите директорию развёртывания на удалённой машине.");
            }

            if (string.IsNullOrWhiteSpace(ProjectEnvironmentFilePath))
            {
                return Localize(
                    "Укажите путь к файлу переменных окружения внутри проекта.");
            }

            if (MaxStoredVersions is < 1 or > 10000)
            {
                return Localize(
                    "Максимальное количество версий должно быть от 1 до 10000.");
            }

            if (!TryParseStorageLimitBytes(out _))
            {
                return Localize(
                    "Лимит размера версий должен быть положительным числом в МБ.");
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
                    return Localize("Укажите command для пользовательской сборки.");
                }

                if (provider == ProjectBuildProvider.Cpp &&
                    string.IsNullOrWhiteSpace(configuration.ToolchainFile))
                {
                    return Localize("Укажите toolchainFile для сборки C++ под Linux.");
                }
            }
            catch (System.IO.IOException exception)
            {
                return Localize(exception.Message);
            }
            catch (ArgumentException)
            {
                return Localize("Проверьте пути в параметрах сборки.");
            }
            catch (System.Text.Json.JsonException)
            {
                return Localize("Параметры сборки содержат некорректный JSON.");
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
