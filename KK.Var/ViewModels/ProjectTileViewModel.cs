using KK.Var.Models;

namespace KK.Var.ViewModels;

public sealed class ProjectTileViewModel
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

    public static ProjectTileViewModel AddTile { get; } = new();
}
