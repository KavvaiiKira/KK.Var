using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Services;
using KK.Var.Views;
using Microsoft.Extensions.DependencyInjection;

namespace KK.Var;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            OpenInitialApplicationWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OpenInitialApplicationWindow(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (Program.UserSettingsStartupException is { } settingsException)
        {
            Program.Services
                .GetRequiredService<ILocalizationService>()
                .SetLanguage(new UserSettings().Language);
            OpenRecoveryWindow(
                desktop,
                StartupRecoveryKind.UserSettings,
                settingsException,
                showImmediately: false);
            return;
        }

        OpenApplicationWindow(
            desktop,
            Program.StartupUserSettings ?? new UserSettings(),
            showImmediately: false);
    }

    private static void OpenApplicationWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        UserSettings settings,
        bool showImmediately)
    {
        Program.Services
            .GetRequiredService<ILocalizationService>()
            .SetLanguage(settings.Language);

        if (Program.DatabaseStartupException is { } databaseException)
        {
            OpenRecoveryWindow(
                desktop,
                StartupRecoveryKind.Database,
                databaseException,
                showImmediately);
            return;
        }

        var mainWindow = Program.Services.GetRequiredService<MainWindow>();
        desktop.MainWindow = mainWindow;
        if (showImmediately)
        {
            mainWindow.Show();
        }
    }

    private static void OpenRecoveryWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        StartupRecoveryKind kind,
        Exception exception,
        bool showImmediately)
    {
        var recoveryWindow = new StartupRecoveryWindow(
            kind,
            exception,
            Program.Services.GetRequiredService<IStartupRecoveryService>());
        recoveryWindow.RecoverySucceeded += async (_, _) =>
        {
            if (kind == StartupRecoveryKind.Database)
            {
                Program.MarkDatabaseRecovered();
            }

            var settings = await Program.Services
                .GetRequiredService<IUserSettingsService>()
                .LoadAsync();
            OpenApplicationWindow(desktop, settings, showImmediately: true);
            recoveryWindow.Close();
        };

        desktop.MainWindow = recoveryWindow;
        if (showImmediately)
        {
            recoveryWindow.Show();
        }
    }

}
