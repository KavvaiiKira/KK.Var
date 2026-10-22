using System;
using Avalonia;
using KK.Var.Configuration;
using KK.Var.Constants;
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

namespace KK.Var
{
    sealed class Program
    {
        private const string ServicesNotInitializedMessage = "Application services are not initialized.";

        private static IHost? _host;

        internal static Exception? DatabaseStartupException { get; private set; }

        internal static UserSettings? StartupUserSettings { get; private set; }

        internal static Exception? UserSettingsStartupException { get; private set; }

        public static IServiceProvider Services =>
            _host?.Services ??
            throw new InvalidOperationException(ServicesNotInitializedMessage);

        [STAThread]
        public static void Main(string[] args)
        {
            WindowsApplicationIdentity.Initialize();
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
                FileName = builder.Configuration[$"{DatabaseOptions.SectionName}:FileName"] ?? ApplicationConstants.DefaultDatabaseFileName,
            };

            var gitHubOptions = new GitHubOptions
            {
                ClientId = builder.Configuration[$"{GitHubOptions.SectionName}:ClientId"] ?? string.Empty,
                Scope = builder.Configuration[$"{GitHubOptions.SectionName}:Scope"] ?? GitHubProtocol.DefaultScope,
            };

            DatabasePaths.EnsureUserDataDirectory();

            builder.Services.AddSingleton(databaseOptions);
            builder.Services.AddSingleton(gitHubOptions);
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite(
                    $"Data Source={DatabasePaths.GetDatabaseFilePath(databaseOptions.FileName)}"));

            builder.Services.AddSingleton<IBuildConfigurationService, BuildConfigurationService>();
            builder.Services.AddSingleton<IProjectSourceService, ProjectSourceService>();
            builder.Services.AddSingleton<IBuildProcessRunner, BuildProcessRunner>();
            builder.Services.AddSingleton<IProjectBuildService, ProjectBuildService>();
            builder.Services.AddSingleton<IArtifactPackagingService, ArtifactPackagingService>();
            builder.Services.AddSingleton<IRemoteCommandRunner, RemoteCommandRunner>();
            builder.Services.AddSingleton<IRemoteDeploymentValidator, RemoteDeploymentValidator>();
            builder.Services.AddSingleton<ISystemdUnitBuilder, SystemdUnitBuilder>();
            builder.Services.AddSingleton<IRemoteHealthCheckService, RemoteHealthCheckService>();
            builder.Services.AddSingleton<IRemoteRecoveryService, RemoteRecoveryService>();
            builder.Services.AddSingleton<IRemoteClientFactory, RemoteClientFactory>();
            builder.Services.AddSingleton<IDeploymentNotificationService, DeploymentNotificationService>();
            builder.Services.AddSingleton<IDeploymentJournal, DeploymentJournal>();
            builder.Services.AddSingleton<IDeploymentExecutor, DeploymentExecutor>();
            builder.Services.AddSingleton<IDeploymentRecoveryService, DeploymentRecoveryService>();
            builder.Services.AddSingleton<IDeploymentHistoryService, DeploymentHistoryService>();
            builder.Services.AddSingleton<IArtifactPathPolicy, ArtifactPathPolicy>();
            builder.Services.AddSingleton<IArtifactStorageRelocationService, ArtifactStorageRelocationService>();
            builder.Services.AddSingleton<IArtifactStorageActivity, ArtifactStorageActivity>();
            builder.Services.AddSingleton<ISourcePreflightService, SourcePreflightService>();
            builder.Services.AddSingleton<IBuildPreflightService, BuildPreflightService>();
            builder.Services.AddSingleton<IStoragePreflightService, StoragePreflightService>();
            builder.Services.AddSingleton<DeploymentHistoryViewModel, DeploymentHistoryViewModel>();
            builder.Services.AddSingleton<GitHubAccountViewModel, GitHubAccountViewModel>();
            builder.Services.AddSingleton<ArtifactStorageViewModel, ArtifactStorageViewModel>();
            builder.Services.AddSingleton<IPreflightCheckFactory, PreflightCheckFactory>();
            builder.Services.AddSingleton<ProjectEnvironmentEditorViewModel, ProjectEnvironmentEditorViewModel>();
            builder.Services.AddSingleton<DeploymentWorkspaceViewModel, DeploymentWorkspaceViewModel>();
            builder.Services.AddSingleton<RemoteMachineEditorViewModel, RemoteMachineEditorViewModel>();
            builder.Services.AddSingleton<IApplicationStartupService, ApplicationStartupService>();
            builder.Services.AddSingleton<IUserSettingsService, UserSettingsService>();
            builder.Services.AddSingleton<IRemoteMachineService, RemoteMachineService>();
            builder.Services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
            builder.Services.AddSingleton<ISshPasswordStore, SshPasswordStore>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
            builder.Services.AddSingleton<IRemoteConnectionService, RemoteConnectionService>();
            builder.Services.AddSingleton<IDeploymentPreflightService, DeploymentPreflightService>();
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
            builder.Services.AddSingleton<IProjectConfigurationTransferService, ProjectConfigurationTransferService>();
            builder.Services.AddSingleton<
                IKKProjectEnvironmentService,
                KKProjectEnvironmentService>();
            builder.Services.AddSingleton<IKKProjectVersionService, KKProjectVersionService>();
            builder.Services.AddSingleton<IArtifactStorageService, ArtifactStorageService>();
            builder.Services.AddSingleton<IVersionCleanupService, VersionCleanupService>();
            builder.Services.AddSingleton<IProjectArtifactService, ProjectArtifactService>();
            builder.Services.AddSingleton<IRemoteDeploymentService, RemoteDeploymentService>();
            builder.Services.AddSingleton<
                IDeploymentOperationQueue,
                DeploymentOperationQueue>();
            builder.Services.AddSingleton<
                IKKProjectDeploymentService,
                KKProjectDeploymentService>();
            builder.Services.AddTransient<GitHubSourceSelectionViewModel>();
            builder.Services.AddSingleton<CreateProjectViewModel>();
            builder.Services.AddSingleton<ProjectDetailsViewModel>();
            builder.Services.AddSingleton<ApplicationSettingsViewModel>();
            builder.Services.AddSingleton<ProjectListViewModel>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainWindow>(services => new MainWindow(
                services.GetRequiredService<ILocalizationService>(),
                services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WindowsDeploymentNotifications>>())
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

        internal static async System.Threading.Tasks.Task<Exception?> InitializeDatabaseAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Run(ApplyDatabaseMigrations);

                DatabaseStartupException = null;
                return null;
            }
            catch (Exception exception)
            {
                DatabaseStartupException = exception;
                return exception;
            }
        }

        internal static void MarkDatabaseRecovered() =>
            DatabaseStartupException = null;

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
