using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.ViewModels;

namespace KK.Var.Views;

public partial class MainWindow : Window
{
    private bool _firstRunHandled;

    public MainWindow()
    {
        InitializeComponent();

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
        ShowSettingsPage(showSettings: false);
    }

    private void SettingsNavigationButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ShowSettingsPage(showSettings: true);
    }

    private void OpenRequiredSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
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
        SetNavigationState(ProjectsNavigationButton, !showSettings);
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
}
