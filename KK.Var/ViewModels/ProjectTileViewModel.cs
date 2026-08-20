using System.Linq;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public sealed class ProjectTileViewModel
{
    public ProjectTileViewModel(
        KKProject project,
        ILocalizationService? localizationService = null)
    {
        Project = project;
        _localizationService = localizationService;
    }

    private ProjectTileViewModel()
    { }

    public KKProject? Project { get; }

    private readonly ILocalizationService? _localizationService;

    public bool IsProject => Project is not null;

    public bool IsAddTile => Project is null;

    public string SourceDisplay =>
        Project?.SourceType == ProjectSourceType.GitHubRepository ?
            $"GitHub · {Project.GitHubRepositoryFullName}" :
            $"{Localize("Локальная папка")} · {Project?.LocalDirectoryPath}";

    public string LastDeploymentDisplay
    {
        get
        {
            var deployment = Project?.Deployments.MaxBy(item => item.StartedAtUtc);
            return deployment is null ?
                Localize("Не выполнялся") :
                deployment.StartedAtUtc.ToLocalTime().ToString("g");
        }
    }

    public string LatestVersionTag =>
        Project?.Versions.MaxBy(version => version.CreatedAtUtc)?.Tag ?? Localize("Нет версий");

    public static ProjectTileViewModel AddTile { get; } = new ProjectTileViewModel();

    private string Localize(string key) => _localizationService?.Get(key) ?? key;
}
