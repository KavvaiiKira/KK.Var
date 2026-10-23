using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class ProjectListViewModel(
        IKKProjectService _projectService,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        public ObservableCollection<KKProject> Projects { get; } = [];

        public ObservableCollection<ProjectTileViewModel> ProjectTiles { get; } = [];

        [ObservableProperty]
        public partial bool IsBusy { get; private set; }

        public Task<KKProject?> GetByIdAsync(Guid projectId) => _projectService.GetByIdAsync(projectId);

        public void Add(KKProject project)
        {
            Projects.Add(project);
            RebuildProjectTiles();
            PublishNotification(LocalizeFormat(Texts.ProjectAdded, project.Name), isError: false);
        }

        public void Update(KKProject project)
        {
            var previous = Projects.FirstOrDefault(item => item.Id == project.Id);
            if (previous is not null)
            {
                Projects[Projects.IndexOf(previous)] = project;
            }
            RebuildProjectTiles();
            PublishNotification(LocalizeFormat(Texts.ProjectUpdated, project.Name), isError: false);
        }

        public override void RefreshLocalization()
        {
            foreach (var tile in ProjectTiles)
            {
                tile.RefreshLocalization();
            }
        }

        public async Task LoadAsync()
        {
            var projects = await _projectService.GetAllAsync();
            Projects.Clear();

            foreach (var project in projects)
            {
                Projects.Add(project);
            }

            RebuildProjectTiles();
        }

        public async Task<bool> DeleteProjectAsync(KKProject project)
        {
            if (IsBusy)
            {
                return false;
            }

            IsBusy = true;

            try
            {
                await _projectService.DeleteAsync(project.Id);

                Projects.Remove(project);

                RebuildProjectTiles();
                PublishNotification(
                    LocalizeFormat(Texts.ProjectDeleted, project.Name),
                    isError: false);

                return true;
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);

                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RebuildProjectTiles()
        {
            ProjectTiles.Clear();

            foreach (var project in Projects.OrderBy(project => project.Name))
            {
                ProjectTiles.Add(new ProjectTileViewModel(project));
            }

            ProjectTiles.Add(ProjectTileViewModel.AddTile);
        }
    }
}
