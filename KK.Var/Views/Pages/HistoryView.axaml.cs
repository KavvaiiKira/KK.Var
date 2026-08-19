using Avalonia.Controls;
using Avalonia.Interactivity;
using KK.Var.ViewModels;

namespace KK.Var.Views.Pages;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private async void LoadMoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadMoreHistoryAsync();
        }
    }
}
