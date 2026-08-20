using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.Enums;
using KK.Var.Services;
using KK.Var.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace KK.Var.Views;

public partial class FirstRunWindow : Window
{
    private const int LastSlideIndex = 2;
    private int _currentSlideIndex;
    private readonly ILocalizationService _localizationService =
        Program.Services.GetRequiredService<ILocalizationService>();

    public FirstRunWindow()
    {
        InitializeComponent();
        UpdateLanguageButton();
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

    private async void LanguageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Owner?.DataContext is not MainViewModel viewModel)
        {
            return;
        }

        await viewModel.SwitchLanguageAsync();
        UpdateLanguageButton();
        UpdateSlide();
    }

    private void UpdateLanguageButton()
    {
        var isEnglish = _localizationService.CurrentLanguage == ApplicationLanguage.English;
        LanguageButton.Content = isEnglish ? "EN" : "RU";
        ToolTip.SetTip(
            LanguageButton,
            _localizationService.Get(
                isEnglish ? "Переключить на русский" : "Переключить на английский"));
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

        NextButton.Content = _currentSlideIndex == LastSlideIndex ?
            _localizationService.Get("Открыть настройки") :
            _localizationService.Get("Далее");
    }
}
