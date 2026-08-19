using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KK.Var.Views.Pages;

public partial class ProjectDetailsView : UserControl
{
    public ProjectDetailsView()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? EditRequested;

    public event EventHandler? DeleteRequested;

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EditButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EditRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }
}
