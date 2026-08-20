using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class ProjectDetailsView : UserControl
{
    private Action? _pendingNavigation;

    public ProjectDetailsView()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? EditRequested;

    public event EventHandler? DeleteRequested;

    public event EventHandler? DeployRequested;

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => BackRequested?.Invoke(this, EventArgs.Empty)))
        {
            return;
        }

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EditButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => EditRequested?.Invoke(this, EventArgs.Empty)))
        {
            return;
        }

        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => DeleteRequested?.Invoke(this, EventArgs.Empty)))
        {
            return;
        }

        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeployButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => DeployRequested?.Invoke(this, EventArgs.Empty)))
        {
            return;
        }

        DeployRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OverviewTab_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(ShowOverviewSection))
        {
            return;
        }

        ShowOverviewSection();
    }

    private void EnvironmentTab_OnClick(object? sender, RoutedEventArgs e)
    {
        ShowSection(EnvironmentPanel, EnvironmentTab);
    }

    private void VersionsTab_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => ShowSection(VersionsPanel, VersionsTab)))
        {
            return;
        }

        ShowSection(VersionsPanel, VersionsTab);
    }

    private void ProjectHistoryTab_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!CanNavigateAway(() => ShowSection(HistoryPanel, ProjectHistoryTab)))
        {
            return;
        }

        ShowSection(HistoryPanel, ProjectHistoryTab);
    }

    private void AddVariableButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.AddEnvironmentVariable();
        }
    }

    private void RemoveVariableButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            (sender as Control)?.DataContext is EnvironmentVariableRowViewModel variable)
        {
            viewModel.RemoveEnvironmentVariable(variable);
        }
    }

    private async void SaveVariablesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SaveEnvironmentVariablesAsync();
        }
    }

    private async void LoadMoreProjectHistoryButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadMoreProjectHistoryAsync();
        }
    }

    private async void RunDeployButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.DeploySelectedProjectAsync();
        }
    }

    private async void RollbackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            (sender as Control)?.DataContext is ProjectVersionItemViewModel version)
        {
            await viewModel.RollbackAsync(version);
        }
    }

    private void DeploymentLog_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        DeploymentLog.CaretIndex = DeploymentLog.Text?.Length ?? 0;
    }

    public void ShowOverviewSection()
    {
        ShowSection(OverviewPanel, OverviewTab);
    }

    public void ShowDeploySection()
    {
        ShowSection(DeployPanel);
    }

    public bool CanNavigateAway(Action pendingNavigation)
    {
        if (EnvironmentPanel.IsVisible &&
            DataContext is MainViewModel { HasUnsavedEnvironmentChanges: true })
        {
            _pendingNavigation = pendingNavigation;
            UnsavedEnvironmentConfirmation.IsVisible = true;
            return false;
        }

        return true;
    }

    private void StayOnEnvironmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _pendingNavigation = null;
        UnsavedEnvironmentConfirmation.IsVisible = false;
    }

    private async void DiscardEnvironmentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var navigation = _pendingNavigation;
        _pendingNavigation = null;
        UnsavedEnvironmentConfirmation.IsVisible = false;

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.DiscardEnvironmentChangesAsync();
        }

        navigation?.Invoke();
    }

    private async void SaveEnvironmentAndContinueButton_OnClick(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel ||
            !await viewModel.SaveEnvironmentVariablesAsync())
        {
            return;
        }

        var navigation = _pendingNavigation;
        _pendingNavigation = null;
        UnsavedEnvironmentConfirmation.IsVisible = false;
        navigation?.Invoke();
    }

    private void ShowSection(Control panel, Button? activeTab = null)
    {
        OverviewPanel.IsVisible = panel == OverviewPanel;
        EnvironmentPanel.IsVisible = panel == EnvironmentPanel;
        VersionsPanel.IsVisible = panel == VersionsPanel;
        HistoryPanel.IsVisible = panel == HistoryPanel;
        DeployPanel.IsVisible = panel == DeployPanel;

        SetTabState(OverviewTab, activeTab == OverviewTab);
        SetTabState(EnvironmentTab, activeTab == EnvironmentTab);
        SetTabState(VersionsTab, activeTab == VersionsTab);
        SetTabState(ProjectHistoryTab, activeTab == ProjectHistoryTab);
    }

    private static void SetTabState(Button button, bool isActive)
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
}
