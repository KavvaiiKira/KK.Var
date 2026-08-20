using System;
using Avalonia;
using KK.Var.Configuration;
using KK.Var.Data;
using KK.Var.Repositories;
using KK.Var.Repositories.Implementations;
using KK.Var.Services;
using KK.Var.Services.Implementations;
using KK.Var.ViewModels;
using KK.Var.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KK.Var;

sealed class Program
{
    private static IHost? _host;

    internal static Exception? DatabaseStartupException { get; private set; }

    internal static UserSettings? StartupUserSettings { get; private set; }

    internal static Exception? UserSettingsStartupException { get; private set; }

    public static IServiceProvider Services =>
        _host?.Services ??
        throw new InvalidOperationException("Application services are not initialized.");

    [STAThread]
    public static void Main(string[] args)
    {
        _host = CreateHost(args);

        try
        {
            _host.Start();
            try
            {
                StartupUserSettings = Services
                    .GetRequiredService<IUserSettingsService>()
                    .LoadAsync()
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                UserSettingsStartupException = exception;
            }

            try
            {
                ApplyDatabaseMigrations();
            }
            catch (Exception exception)
            {
                DatabaseStartupException = exception;
            }
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
            FileName = builder.Configuration[$"{DatabaseOptions.SectionName}:FileName"] ?? "kk-var.db",
        };

        var gitHubOptions = new GitHubOptions
        {
            ClientId = builder.Configuration[$"{GitHubOptions.SectionName}:ClientId"] ?? string.Empty,
            Scope = builder.Configuration[$"{GitHubOptions.SectionName}:Scope"] ?? "repo read:user",
        };

        DatabasePaths.EnsureUserDataDirectory();

        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddSingleton(gitHubOptions);
        builder.Services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(
                $"Data Source={DatabasePaths.GetDatabaseFilePath(databaseOptions.FileName)}"));

        builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
        builder.Services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
        builder.Services.AddSingleton<ISshPasswordStore, SshPasswordStore>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<IRemoteConnectionService, RemoteConnectionService>();
        builder.Services.AddSingleton<IGitHubTokenStore, GitHubTokenStore>();
        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        builder.Services.AddSingleton<
            IGitHubAuthenticationService,
            GitHubAuthenticationService>();
        builder.Services.AddSingleton<IKKProjectRepository, KKProjectRepository>();
        builder.Services.AddSingleton<
            IKKProjectEnvironmentVariableRepository,
            KKProjectEnvironmentVariableRepository>();
        builder.Services.AddSingleton<IKKProjectVersionRepository, KKProjectVersionRepository>();
        builder.Services.AddSingleton<
            IKKProjectDeploymentRepository,
            KKProjectDeploymentRepository>();

        builder.Services.AddSingleton<IKKProjectService, KKProjectService>();
        builder.Services.AddSingleton<
            IKKProjectEnvironmentService,
            KKProjectEnvironmentService>();
        builder.Services.AddSingleton<IKKProjectVersionService, KKProjectVersionService>();
        builder.Services.AddSingleton<IProjectArtifactService, ProjectArtifactService>();
        builder.Services.AddSingleton<IRemoteDeploymentService, RemoteDeploymentService>();
        builder.Services.AddSingleton<
            IKKProjectDeploymentService,
            KKProjectDeploymentService>();
        builder.Services.AddSingleton<CreateProjectViewModel>();
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

    internal static void MarkDatabaseRecovered()
    {
        DatabaseStartupException = null;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
