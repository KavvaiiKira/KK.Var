using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        public void RefreshLocalization()
        {
            GitHubBranchSelection.RefreshLocalization();
            var sourceType = SelectedSourceType;
            var buildProvider = SelectedBuildProvider;
            var healthCheck = SelectedHealthCheckType;
            _isResetting = true;
            BuildEditor.IsResetting = true;

            SourceTypes.Clear();
            SourceTypes.Add(new EnumOption(ProjectSourceType.LocalDirectory, Localize("Локальная папка")));
            SourceTypes.Add(new EnumOption(ProjectSourceType.GitHubRepository, Localize("Репозиторий GitHub")));

            BuildProviders.Clear();
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Unknown, Localize("Определить автоматически")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.DotNet, Localize(".NET")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Python, Localize("Python")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Go, Localize("Go")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Cpp, Localize("C++")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Custom, Localize("Свой сценарий")));

            HealthCheckTypes.Clear();
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.None, Localize("Без проверки")));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.StabilityDelay, Localize("Задержка стабильности")));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Http, Localize("HTTP-запрос")));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Tcp, Localize("TCP-порт")));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Command, Localize("Команда")));

            SelectedSourceType = sourceType;
            SelectedBuildProvider = buildProvider;
            SelectedHealthCheckType = healthCheck;
            _isResetting = false;
            BuildEditor.IsResetting = false;

            OnPropertyChanged(nameof(IsLocalSource));
            OnPropertyChanged(nameof(IsGitHubSource));
            OnPropertyChanged(nameof(IsHealthCheckEnabled));
            OnPropertyChanged(nameof(IsStabilityDelayHealthCheck));
            OnPropertyChanged(nameof(IsHttpHealthCheck));
            OnPropertyChanged(nameof(IsTcpHealthCheck));
            OnPropertyChanged(nameof(IsCommandHealthCheck));
            OnPropertyChanged(nameof(HasHealthCheckRetrySettings));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(SaveButtonText));
        }

        public string Localize(string key) => _localizationService.Get(key);
    }
}
