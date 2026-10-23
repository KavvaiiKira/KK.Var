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

            RegisterApplicationServices(builder.Services);

            return builder.Build();
        }

        internal static void RegisterApplicationServices(IServiceCollection services)
        {
            services.AddSingleton<IBuildConfigurationService, BuildConfigurationService>();
            services.AddSingleton<IProjectSourceService, ProjectSourceService>();
            services.AddSingleton<IBuildProcessRunner, BuildProcessRunner>();
            services.AddSingleton<IProjectBuildService, ProjectBuildService>();
            services.AddSingleton<IArtifactPackagingService, ArtifactPackagingService>();
            services.AddSingleton<IRemoteCommandRunner, RemoteCommandRunner>();
            services.AddSingleton<IRemoteDeploymentValidator, RemoteDeploymentValidator>();
            services.AddSingleton<ISystemdUnitBuilder, SystemdUnitBuilder>();
            services.AddSingleton<IRemoteHealthCheckService, RemoteHealthCheckService>();
            services.AddSingleton<IRemoteRecoveryService, RemoteRecoveryService>();
            services.AddSingleton<IRemoteClientFactory, RemoteClientFactory>();
            services.AddSingleton<IDeploymentNotificationService, DeploymentNotificationService>();
            services.AddSingleton<IDeploymentJournal, DeploymentJournal>();
            services.AddSingleton<IDeploymentExecutor, DeploymentExecutor>();
            services.AddSingleton<IDeploymentRecoveryService, DeploymentRecoveryService>();
            services.AddSingleton<IDeploymentHistoryService, DeploymentHistoryService>();
            services.AddSingleton<IArtifactPathPolicy, ArtifactPathPolicy>();
            services.AddSingleton<IArtifactStorageRelocationService, ArtifactStorageRelocationService>();
            services.AddSingleton<IArtifactStorageActivity, ArtifactStorageActivity>();
            services.AddSingleton<ISourcePreflightService, SourcePreflightService>();
            services.AddSingleton<IBuildPreflightService, BuildPreflightService>();
            services.AddSingleton<IStoragePreflightService, StoragePreflightService>();
            services.AddSingleton<DeploymentHistoryViewModel, DeploymentHistoryViewModel>();
            services.AddSingleton<GitHubAccountViewModel, GitHubAccountViewModel>();
            services.AddSingleton<ArtifactStorageViewModel, ArtifactStorageViewModel>();
            services.AddSingleton<IPreflightCheckFactory, PreflightCheckFactory>();
            services.AddSingleton<ProjectEnvironmentEditorViewModel, ProjectEnvironmentEditorViewModel>();
            services.AddSingleton<DeploymentWorkspaceViewModel, DeploymentWorkspaceViewModel>();
            services.AddSingleton<RemoteMachineEditorViewModel, RemoteMachineEditorViewModel>();
            services.AddSingleton<IApplicationStartupService, ApplicationStartupService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<IRemoteMachineService, RemoteMachineService>();
            services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
            services.AddSingleton<ISshPasswordStore, SshPasswordStore>();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton<IRemoteConnectionService, RemoteConnectionService>();
            services.AddSingleton<IDeploymentPreflightService, DeploymentPreflightService>();
            services.AddSingleton<IGitHubTokenStore, GitHubTokenStore>();
            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<
                IGitHubAuthenticationService,
                GitHubAuthenticationService>();
            services.AddSingleton<IKKProjectRepository, KKProjectRepository>();
            services.AddSingleton<
                IKKProjectEnvironmentVariableRepository,
                KKProjectEnvironmentVariableRepository>();
            services.AddSingleton<IKKProjectVersionRepository, KKProjectVersionRepository>();
            services.AddSingleton<
                IKKProjectDeploymentRepository,
                KKProjectDeploymentRepository>();

            services.AddSingleton<IKKProjectService, KKProjectService>();
            services.AddSingleton<IProjectConfigurationTransferService, ProjectConfigurationTransferService>();
            services.AddSingleton<
                IKKProjectEnvironmentService,
                KKProjectEnvironmentService>();
            services.AddSingleton<IKKProjectVersionService, KKProjectVersionService>();
            services.AddSingleton<IArtifactStorageService, ArtifactStorageService>();
            services.AddSingleton<IVersionCleanupService, VersionCleanupService>();
            services.AddSingleton<IProjectArtifactService, ProjectArtifactService>();
            services.AddSingleton<IRemoteDeploymentService, RemoteDeploymentService>();
            services.AddSingleton<
                IDeploymentOperationQueue,
                DeploymentOperationQueue>();
            services.AddSingleton<
                IKKProjectDeploymentService,
                KKProjectDeploymentService>();
            services.AddTransient<GitHubSourceSelectionViewModel>();
            services.AddSingleton<CreateProjectViewModel>();
            services.AddSingleton<ProjectDetailsViewModel>();
            services.AddSingleton<ApplicationSettingsViewModel>();
            services.AddSingleton<ProjectListViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>(services => new MainWindow(
                services.GetRequiredService<ILocalizationService>(),
                services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WindowsDeploymentNotifications>>())
            {
                DataContext = services.GetRequiredService<MainViewModel>(),
            });
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
