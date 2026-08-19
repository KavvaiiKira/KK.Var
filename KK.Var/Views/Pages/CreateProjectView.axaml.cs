using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KK.Var.Models;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class CreateProjectView : UserControl
{
    public CreateProjectView()
    {
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public void BeginCreation()
    {
        DiscardConfirmation.IsVisible = false;

        if (DataContext is CreateProjectViewModel viewModel)
        {
            viewModel.Reset();
        }
    }

    public void BeginEditing(KKProject project)
    {
        DiscardConfirmation.IsVisible = false;

        if (DataContext is CreateProjectViewModel viewModel)
        {
            viewModel.LoadProject(project);
        }
    }

    public void RequestClose()
    {
        if (DataContext is CreateProjectViewModel { HasUnsavedChanges: true })
        {
            DiscardConfirmation.IsVisible = true;
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RequestClose();
    }

    private async void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateProjectViewModel viewModel &&
            await viewModel.SaveAsync())
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async void BrowseFolderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateProjectViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Выберите папку проекта",
                AllowMultiple = false,
            });

        if (folders.Count > 0)
        {
            viewModel.LocalDirectoryPath = folders[0].Path.LocalPath;
        }
    }

    private void ContinueEditingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DiscardConfirmation.IsVisible = false;
    }

    private void DiscardButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateProjectViewModel viewModel)
        {
            viewModel.Reset();
        }

        DiscardConfirmation.IsVisible = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
