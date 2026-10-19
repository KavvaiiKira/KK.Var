using System;
using System.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public partial class MainViewModel
{
    private Guid? _gitHubSourceProjectId;
    private string? _projectGitHubBranch;
    private string? _projectGitHubRepository;

    public GitHubSourceSelectionViewModel DeploymentGitHubSource { get; } = new GitHubSourceSelectionViewModel();

    public bool IsSelectedProjectGitHub => SelectedProject?.SourceType == ProjectSourceType.GitHubRepository;

    private void ConfigureDeploymentGitHubSource(KKProject? project)
    {
        if (_gitHubSourceProjectId != project?.Id ||
            _projectGitHubBranch != project?.GitHubBranch ||
            _projectGitHubRepository != project?.GitHubRepositoryFullName)
        {
            _gitHubSourceProjectId = project?.Id;
            _projectGitHubBranch = project?.GitHubBranch;
            _projectGitHubRepository = project?.GitHubRepositoryFullName;
            var activeState = SelectedDeploymentState is { IsActive: true } state ? state : null;
            DeploymentGitHubSource.Configure(IsSelectedProjectGitHub ? project?.GitHubRepositoryFullName : null,
                activeState?.GitHubBranch ?? project?.GitHubBranch);
            if (activeState?.GitHubCommit is { } commit)
            {
                DeploymentGitHubSource.Commit = commit;
            }
        }
        OnPropertyChanged(nameof(IsSelectedProjectGitHub));
        OnPropertyChanged(nameof(CanCheckDeployment));
    }

    private void DeploymentGitHubSource_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GitHubSourceSelectionViewModel.SelectedBranch) or
            nameof(GitHubSourceSelectionViewModel.Commit))
        {
            InvalidateDeploymentPreflight(SelectedProject?.Id);
        }
        OnPropertyChanged(nameof(CanCheckDeployment));
    }
}
