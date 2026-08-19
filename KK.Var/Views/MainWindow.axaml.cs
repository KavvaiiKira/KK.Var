using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KK.Var.Views;

public partial class MainWindow : Window
{
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
        TitleBar.CornerRadius = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(14, 14, 0, 0);
        StatusBar.CornerRadius = isMaximized
            ? new CornerRadius(0)
            : new CornerRadius(0, 0, 14, 14);
    }
}
