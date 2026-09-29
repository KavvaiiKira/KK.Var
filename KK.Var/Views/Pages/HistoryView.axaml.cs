using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    public event Action<DeploymentHistoryItemViewModel>? DeploymentSelected;

    private void DeploymentItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.DataContext is DeploymentHistoryItemViewModel item)
        {
            DeploymentSelected?.Invoke(item);
        }
    }

    private async void LoadMoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadMoreHistoryAsync();
        }
    }
}
