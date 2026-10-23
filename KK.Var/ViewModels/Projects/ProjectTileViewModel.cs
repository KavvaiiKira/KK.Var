using System.Linq;
using KK.Var.Enums;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public sealed class ProjectTileViewModel : ViewModelBase
    {
        public ProjectTileViewModel(KKProject project)
        {
            Project = project;
        }

        private ProjectTileViewModel()
        {
        }

        public KKProject? Project { get; }

        public bool IsProject => Project is not null;

        public bool IsAddTile => Project is null;

        public string ProjectName => Project?.Name ?? string.Empty;

        public string RemoteServiceName => Project?.RemoteServiceName ?? string.Empty;

        public string RemoteExecutablePath => Project?.RemoteExecutablePath ?? string.Empty;

        public string SourceDisplay =>
            Project?.SourceType == ProjectSourceType.GitHubRepository ?
                $"GitHub · {Project.GitHubRepositoryFullName}" :
                $"{Texts.LocalFolder} · {Project?.LocalDirectoryPath}";

        public string LastDeploymentDisplay
        {
            get
            {
                var deployment = Project?.Deployments.MaxBy(item => item.StartedAtUtc);
                return deployment is null ?
                    Texts.Never :
                    deployment.StartedAtUtc.ToLocalTime().ToString("g");
            }
        }

        public string LatestVersionTag =>
            Project?.Versions.MaxBy(version => version.CreatedAtUtc)?.Tag ?? Texts.NoVersions;

        public static ProjectTileViewModel AddTile { get; } = new ProjectTileViewModel();

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(SourceDisplay));
            OnPropertyChanged(nameof(LastDeploymentDisplay));
            OnPropertyChanged(nameof(LatestVersionTag));
        }
    }
}
