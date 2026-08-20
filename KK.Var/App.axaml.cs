using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            var settings = Program.Services
                .GetRequiredService<Services.IUserSettingsService>()
                .LoadAsync()
                .GetAwaiter()
                .GetResult();
            Program.Services
                .GetRequiredService<Services.ILocalizationService>()
                .SetLanguage(settings.Language);
            desktop.MainWindow = Program.Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
