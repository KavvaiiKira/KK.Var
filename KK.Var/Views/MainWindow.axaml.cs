using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.Models;
using KK.Var.ViewModels;

namespace KK.Var.Views;

public partial class MainWindow : Window
{
    private bool _firstRunHandled;
    private bool _showSettingsAfterProjectEditorClose;
    private bool _showHistoryAfterProjectEditorClose;
    private KKProject? _projectPendingDelete;

    public MainWindow()
    {
        InitializeComponent();
        CreateProjectPage.CloseRequested += CreateProjectPage_OnCloseRequested;
        ProjectDetailsPage.BackRequested += ProjectDetailsPage_OnBackRequested;
        ProjectDetailsPage.EditRequested += ProjectDetailsPage_OnEditRequested;
        ProjectDetailsPage.DeleteRequested += ProjectDetailsPage_OnDeleteRequested;
        ProjectDetailsPage.DeployRequested += ProjectDetailsPage_OnDeployRequested;

        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty)
            {
                UpdateWindowChrome();
            }
        };

        UpdateWindowChrome();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizedState();
            return;
        }

        BeginMoveDrag(e);
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizedState();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectDetailsPage.IsVisible &&
            !ProjectDetailsPage.CanNavigateAway(Close))
        {
            return;
        }

        Close();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_firstRunHandled || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _firstRunHandled = true;
        await viewModel.LoadSettingsAsync();
        UpdateProjectsAccessState(viewModel);

        if (viewModel.Settings.IsFirstRunCompleted)
        {
            return;
        }

        var firstRunWindow = new FirstRunWindow();

        ModalBackdrop.IsVisible = true;

        try
        {
            _ = await firstRunWindow.ShowDialog<bool>(this);
        }
        finally
        {
            ModalBackdrop.IsVisible = false;
        }

        viewModel.RequireFirstRunSetup();
        ShowSettingsPage(showSettings: true);
    }

    private void ProjectsNavigationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectDetailsPage.IsVisible &&
            !ProjectDetailsPage.CanNavigateAway(
                () => ProjectsNavigationButton_OnClick(sender, e)))
        {
            return;
        }

        if (CreateProjectPage.IsVisible)
        {
            _showSettingsAfterProjectEditorClose = false;
            _showHistoryAfterProjectEditorClose = false;
            CreateProjectPage.RequestClose();
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        ShowSettingsPage(showSettings: false);
    }

    private void SettingsNavigationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectDetailsPage.IsVisible &&
            !ProjectDetailsPage.CanNavigateAway(
                () => SettingsNavigationButton_OnClick(sender, e)))
        {
            return;
        }

        if (CreateProjectPage.IsVisible)
        {
            _showSettingsAfterProjectEditorClose = true;
            _showHistoryAfterProjectEditorClose = false;
            CreateProjectPage.RequestClose();
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        ShowSettingsPage(showSettings: true);
    }

    private async void HistoryNavigationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectDetailsPage.IsVisible &&
            !ProjectDetailsPage.CanNavigateAway(
                () => HistoryNavigationButton_OnClick(sender, e)))
        {
            return;
        }

        if (CreateProjectPage.IsVisible)
        {
            _showSettingsAfterProjectEditorClose = false;
            _showHistoryAfterProjectEditorClose = true;
            CreateProjectPage.RequestClose();
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
            await viewModel.LoadHistoryAsync();
        }

        ShowHistoryPage();
    }

    private void NewProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ProjectDetailsPage.IsVisible &&
            !ProjectDetailsPage.CanNavigateAway(
                () => NewProjectButton_OnClick(sender, e)))
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.IsRemoteMachineConfigured)
        {
            ShowSettingsPage(showSettings: true);
            return;
        }

        _showSettingsAfterProjectEditorClose = false;
        viewModel.ClearStatusNotification();
        CreateProjectPage.BeginCreation();
        ShowProjectEditorPage();
    }

    private async void OpenProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProject(sender);

        if (project is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.ClearStatusNotification();
        viewModel.SelectedProject = project;
        await viewModel.LoadProjectDetailsAsync(project);
        ProjectDetailsPage.ShowOverviewSection();
        ShowProjectDetailsPage();
    }

    private async void DeployProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProject(sender);

        if (project is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.ClearStatusNotification();
        viewModel.SelectedProject = project;
        await viewModel.LoadProjectDetailsAsync(project);
        ProjectDetailsPage.ShowDeploySection();
        ShowProjectDetailsPage();
    }

    private void EditProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProject(sender);

        if (project is not null)
        {
            BeginProjectEditing(project);
        }
    }

    private void DeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProject(sender);

        if (project is not null)
        {
            ShowDeleteProjectConfirmation(project);
        }
    }

    private async void CreateProjectPage_OnCloseRequested(object? sender, EventArgs e)
    {
        CreateProjectPage.IsVisible = false;

        if (DataContext is MainViewModel projectViewModel &&
            projectViewModel.TakeCreatedProjectForNavigation() is { } createdProject)
        {
            projectViewModel.SelectedProject = createdProject;
            await projectViewModel.LoadProjectDetailsAsync(createdProject);
            ProjectDetailsPage.ShowOverviewSection();
            ShowProjectDetailsPage();
            _showHistoryAfterProjectEditorClose = false;
            _showSettingsAfterProjectEditorClose = false;
            return;
        }

        if (_showHistoryAfterProjectEditorClose && DataContext is MainViewModel historyViewModel)
        {
            historyViewModel.ClearStatusNotification();
            await historyViewModel.LoadHistoryAsync();
            ShowHistoryPage();
            _showHistoryAfterProjectEditorClose = false;
            _showSettingsAfterProjectEditorClose = false;
            return;
        }

        if (_showSettingsAfterProjectEditorClose && DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        ShowSettingsPage(_showSettingsAfterProjectEditorClose);
        _showSettingsAfterProjectEditorClose = false;
        _showHistoryAfterProjectEditorClose = false;
    }

    private void ProjectDetailsPage_OnBackRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        ShowSettingsPage(showSettings: false);
    }

    private void ProjectDetailsPage_OnEditRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel { SelectedProject: { } project })
        {
            BeginProjectEditing(project);
        }
    }

    private void ProjectDetailsPage_OnDeleteRequested(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel { SelectedProject: { } project })
        {
            ShowDeleteProjectConfirmation(project);
        }
    }

    private void ProjectDetailsPage_OnDeployRequested(object? sender, EventArgs e)
    {
        ProjectDetailsPage.ShowDeploySection();
    }

    private void BeginProjectEditing(KKProject project)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        _showSettingsAfterProjectEditorClose = false;
        _showHistoryAfterProjectEditorClose = false;
        CreateProjectPage.BeginEditing(project);
        ShowProjectEditorPage();
    }

    private void ShowProjectEditorPage()
    {
        ProjectsPage.IsVisible = false;
        SettingsPage.IsVisible = false;
        ProjectDetailsPage.IsVisible = false;
        HistoryPage.IsVisible = false;
        CreateProjectPage.IsVisible = true;
        SetNavigationState(ProjectsNavigationButton, isActive: true);
        SetNavigationState(HistoryNavigationButton, isActive: false);
        SetNavigationState(SettingsNavigationButton, isActive: false);
    }

    private void ShowProjectDetailsPage()
    {
        ProjectsPage.IsVisible = false;
        SettingsPage.IsVisible = false;
        CreateProjectPage.IsVisible = false;
        HistoryPage.IsVisible = false;
        ProjectDetailsPage.IsVisible = true;
        SetNavigationState(ProjectsNavigationButton, isActive: true);
        SetNavigationState(HistoryNavigationButton, isActive: false);
        SetNavigationState(SettingsNavigationButton, isActive: false);
    }

    private void ShowHistoryPage()
    {
        ProjectsPage.IsVisible = false;
        SettingsPage.IsVisible = false;
        CreateProjectPage.IsVisible = false;
        ProjectDetailsPage.IsVisible = false;
        HistoryPage.IsVisible = true;
        SetNavigationState(ProjectsNavigationButton, isActive: false);
        SetNavigationState(HistoryNavigationButton, isActive: true);
        SetNavigationState(SettingsNavigationButton, isActive: false);
    }

    private void ShowDeleteProjectConfirmation(KKProject project)
    {
        _projectPendingDelete = project;
        DeleteProjectMessage.Text = $"Проект «{project.Name}» будет удалён без возможности отмены.";
        DeleteProjectConfirmation.IsVisible = true;
    }

    private void CancelDeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _projectPendingDelete = null;
        DeleteProjectConfirmation.IsVisible = false;
    }

    private async void ConfirmDeleteProjectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_projectPendingDelete is null || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var project = _projectPendingDelete;
        DeleteProjectConfirmation.IsVisible = false;
        _projectPendingDelete = null;

        if (await viewModel.DeleteProjectAsync(project))
        {
            ShowSettingsPage(showSettings: false);
        }
    }

    private void OpenRequiredSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ClearStatusNotification();
        }

        ShowSettingsPage(showSettings: true);
    }

    private void ToggleMaximizedState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void UpdateWindowChrome()
    {
        var isMaximized = WindowState == WindowState.Maximized;

        WindowFrame.Margin = new Thickness(isMaximized ? 0 : 8);
        WindowFrame.BorderThickness = new Thickness(isMaximized ? 0 : 1);
        WindowFrame.CornerRadius = new CornerRadius(isMaximized ? 0 : 14);
        ModalBackdrop.CornerRadius = new CornerRadius(isMaximized ? 0 : 14);
        TitleBar.CornerRadius = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(14, 14, 0, 0);
        StatusBar.CornerRadius = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(0, 0, 14, 14);
    }

    private void ShowSettingsPage(bool showSettings)
    {
        if (DataContext is MainViewModel viewModel)
        {
            if (!showSettings && SettingsPage.IsVisible)
            {
                viewModel.ClearSettingsStatus();
            }

            UpdateProjectsAccessState(viewModel);
        }

        ProjectsPage.IsVisible = !showSettings;
        SettingsPage.IsVisible = showSettings;
        CreateProjectPage.IsVisible = false;
        ProjectDetailsPage.IsVisible = false;
        HistoryPage.IsVisible = false;
        SetNavigationState(ProjectsNavigationButton, !showSettings);
        SetNavigationState(HistoryNavigationButton, false);
        SetNavigationState(SettingsNavigationButton, showSettings);
    }

    private void UpdateProjectsAccessState(MainViewModel viewModel)
    {
        var isBlocked = !viewModel.IsRemoteMachineConfigured;

        ProjectsAccessBackdrop.IsVisible = isBlocked;
        ProjectsHeader.Opacity = isBlocked ? 0.28 : 1;
        ProjectsList.Opacity = isBlocked ? 0.28 : 1;
        ProjectsHeader.IsHitTestVisible = !isBlocked;
        ProjectsList.IsHitTestVisible = !isBlocked;
    }

    private static void SetNavigationState(Button button, bool isActive)
    {
        if (isActive && !button.Classes.Contains("active"))
        {
            button.Classes.Add("active");
        }
        else if (!isActive)
        {
            button.Classes.Remove("active");
        }
    }

    private static KKProject? GetProject(object? sender) =>
        (sender as Control)?.DataContext is ProjectTileViewModel tile
            ? tile.Project
            : null;
}
