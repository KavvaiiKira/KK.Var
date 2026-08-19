using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace KK.Var.Views;

public partial class FirstRunWindow : Window
{
    private const int LastSlideIndex = 2;
    private int _currentSlideIndex;

    public FirstRunWindow()
    {
        InitializeComponent();
        UpdateSlide();
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void BackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentSlideIndex > 0)
        {
            _currentSlideIndex--;
            UpdateSlide();
        }
    }

    private void NextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentSlideIndex == LastSlideIndex)
        {
            Close(true);
            return;
        }

        _currentSlideIndex++;
        UpdateSlide();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void UpdateSlide()
    {
        WelcomeSlide.IsVisible = _currentSlideIndex == 0;
        WorkflowSlide.IsVisible = _currentSlideIndex == 1;
        SetupSlide.IsVisible = _currentSlideIndex == 2;

        FirstIndicator.Opacity = _currentSlideIndex == 0 ? 1 : 0.25;
        SecondIndicator.Opacity = _currentSlideIndex == 1 ? 1 : 0.25;
        ThirdIndicator.Opacity = _currentSlideIndex == 2 ? 1 : 0.25;

        BackButton.IsEnabled = _currentSlideIndex > 0;
        NextButton.Content = _currentSlideIndex == LastSlideIndex
            ? "Открыть настройки"
            : "Далее";
    }
}
