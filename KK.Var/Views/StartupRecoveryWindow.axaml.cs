using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.Enums;
using KK.Var.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KK.Var.Views;

public partial class StartupRecoveryWindow : Window
{
    private readonly StartupRecoveryKind _kind;
    private readonly IStartupRecoveryService _recoveryService;

    public StartupRecoveryWindow()
        : this(
            StartupRecoveryKind.UserSettings,
            new InvalidOperationException("Startup recovery is required."),
            Program.Services.GetRequiredService<IStartupRecoveryService>())
    {
    }

    public StartupRecoveryWindow(
        StartupRecoveryKind kind,
        Exception exception,
        IStartupRecoveryService recoveryService)
    {
        _kind = kind;
        _recoveryService = recoveryService;
        InitializeComponent();

        var localization = Program.Services.GetRequiredService<ILocalizationService>();

        TitleText.Text = kind == StartupRecoveryKind.UserSettings
            ? localization.Get("Не удалось прочитать настройки")
            : localization.Get("Не удалось открыть базу данных");
        DescriptionText.Text = kind == StartupRecoveryKind.UserSettings
            ? localization.Get("Настройки повреждены или недоступны. Можно сохранить исходный файл в резервной копии и начать с чистых настроек.")
            : localization.Get("SQLite-база повреждена или её схема не может быть обновлена. Можно сохранить все файлы базы в резервной копии и создать новую базу.");
        ErrorText.Text = exception.Message;
        OpenDataDirectoryButton.Content = localization.Get("Открыть папку данных");
        CloseButton.Content = localization.Get("Завершить");
        RecoverButton.Content = localization.Get("Создать копию и сбросить");
    }

    public event EventHandler? RecoverySucceeded;

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OpenDataDirectoryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _recoveryService.OpenUserDataDirectory();
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RecoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RecoverButton.IsEnabled = false;
        var localization = Program.Services.GetRequiredService<ILocalizationService>();
        StatusText.Text = localization.Get("Создание резервной копии...");

        try
        {
            var backupDirectory = await _recoveryService.BackupAndResetAsync(_kind);
            StatusText.Text = localization.Format("Резервная копия: {0}", backupDirectory);
            RecoverySucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            StatusText.Text = localization.Get("Восстановление не выполнено.");
            RecoverButton.IsEnabled = true;
        }
    }
}
