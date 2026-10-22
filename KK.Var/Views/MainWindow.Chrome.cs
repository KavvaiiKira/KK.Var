using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KK.Var.Views
{
    public partial class MainWindow
    {
        private const double MaximumNormalWidth = 1920;

        private const double MaximumNormalHeight = 1080;

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

        private void ResizeNorth_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.North, e);

        private void ResizeSouth_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.South, e);

        private void ResizeWest_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.West, e);

        private void ResizeEast_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.East, e);

        private void ResizeNorthWest_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.NorthWest, e);

        private void ResizeNorthEast_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.NorthEast, e);

        private void ResizeSouthWest_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.SouthWest, e);

        private void ResizeSouthEast_OnPointerPressed(object? sender, PointerPressedEventArgs e) =>
            BeginWindowResize(WindowEdge.SouthEast, e);

        private void BeginWindowResize(WindowEdge edge, PointerPressedEventArgs e)
        {
            if (WindowState == WindowState.Normal &&
                e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(edge, e);
            }
        }

        private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void MaximizeButton_OnClick(object? sender, RoutedEventArgs e) =>
            ToggleMaximizedState();

        private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        {
            if (!CanCloseApplication())
            {
                return;
            }

            if (ProjectDetailsPage.IsVisible &&
                !ProjectDetailsPage.CanNavigateAway(Close))
            {
                return;
            }

            Close();
        }

        private void ToggleMaximizedState() =>
            WindowState =
                WindowState == WindowState.Maximized ?
                    WindowState.Normal :
                    WindowState.Maximized;

        private void UpdateWindowChrome()
        {
            var isMaximized = WindowState == WindowState.Maximized;

            MaxWidth = isMaximized ? double.PositiveInfinity : MaximumNormalWidth;
            MaxHeight = isMaximized ? double.PositiveInfinity : MaximumNormalHeight;
            ResizeHandles.IsVisible = !isMaximized;

            WindowFrame.Margin = new Thickness(isMaximized ? 0 : 8);
            WindowFrame.BorderThickness = new Thickness(isMaximized ? 0 : 1);
            WindowFrame.CornerRadius = new CornerRadius(isMaximized ? 0 : 14);
            ModalBackdrop.CornerRadius = new CornerRadius(isMaximized ? 0 : 14);

            TitleBar.CornerRadius = isMaximized ?
                new CornerRadius(0) :
                new CornerRadius(14, 14, 0, 0);

            StatusBar.CornerRadius = isMaximized ?
                new CornerRadius(0) :
                new CornerRadius(0, 0, 14, 14);
        }
    }
}
