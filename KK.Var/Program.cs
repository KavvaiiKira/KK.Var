using System;
using Avalonia;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Services;
using KK.Var.ViewModels;
using KK.Var.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KK.Var;

sealed class Program
{
    private static IHost? _host;

    public static IServiceProvider Services =>
        _host?.Services
        ?? throw new InvalidOperationException("Application services are not initialized.");

    [STAThread]
    public static void Main(string[] args)
    {
        _host = CreateHost(args);

        try
        {
            _host.Start();
            ApplyDatabaseMigrations();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
            _host = null;
        }
    }

    private static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        var databaseOptions = new DatabaseOptions
        {
            FileName = builder.Configuration[$"{DatabaseOptions.SectionName}:FileName"]
                ?? "kk-var.db",
        };

        DatabasePaths.EnsureUserDataDirectory();

        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(
                $"Data Source={DatabasePaths.GetDatabaseFilePath(databaseOptions.FileName)}"));

        builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>(services => new MainWindow
        {
            DataContext = services.GetRequiredService<MainViewModel>(),
        });

        return builder.Build();
    }

    private static void ApplyDatabaseMigrations()
    {
        using var dbContext = Services
            .GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContext();

        dbContext.Database.Migrate();
    }

    // Avalonia configuration, also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
