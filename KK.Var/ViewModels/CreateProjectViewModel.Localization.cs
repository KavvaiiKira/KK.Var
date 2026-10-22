using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

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
            SourceTypes.Add(new EnumOption(ProjectSourceType.LocalDirectory, Texts.LocalFolder));
            SourceTypes.Add(new EnumOption(ProjectSourceType.GitHubRepository, Texts.GitHubRepository));

            BuildProviders.Clear();
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Unknown, Texts.DetectAutomatically));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.DotNet, Localize(".NET")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Python, Localize("Python")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Go, Localize("Go")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Cpp, Localize("C++")));
            BuildProviders.Add(new EnumOption(ProjectBuildProvider.Custom, Texts.CustomScript));

            HealthCheckTypes.Clear();
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.None, Texts.NoCheck));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.StabilityDelay, Texts.StabilityDelay));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Http, Texts.HTTPRequest));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Tcp, Texts.TCPPort));
            HealthCheckTypes.Add(new EnumOption(ProjectHealthCheckType.Command, Texts.Command));

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
