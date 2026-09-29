using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KK.Var.Views.Pages;

public partial class DeploymentDetailsView : UserControl
{
    public DeploymentDetailsView()
    {
        InitializeComponent();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? RepeatDeployRequested;

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RepeatDeployButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RepeatDeployRequested?.Invoke(this, EventArgs.Empty);
    }
}
