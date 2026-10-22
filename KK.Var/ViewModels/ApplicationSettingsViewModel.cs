using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Configuration;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class ApplicationSettingsViewModel(
        IUserSettingsService settingsService,
        IApplicationStartupService startupService,
        RemoteMachineEditorViewModel machineEditor,
        ArtifactStorageViewModel artifactStorage,
        GitHubAccountViewModel gitHubAccount,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private bool _loadingNotificationPreference;

        [ObservableProperty]
        public partial UserSettings Settings { get; private set; } = new UserSettings();

        [ObservableProperty]
        public partial bool IsFirstRunSetupRequired { get; private set; }

        [ObservableProperty]
        public partial bool DeploymentNotificationsEnabled { get; set; } = true;

        public async Task LoadAsync()
        {
            Settings = await settingsService.LoadAsync();
            _loadingNotificationPreference = true;
            DeploymentNotificationsEnabled = Settings.DeploymentNotificationsEnabled;
            _loadingNotificationPreference = false;
            await machineEditor.ReloadMachineProfilesAsync();
            await artifactStorage.RefreshArtifactStorageSummaryAsync();
            machineEditor.RemoteMachineArchitecture = machineEditor.FormatArchitecture(machineEditor.MachineConnection.Architecture);
            machineEditor.AuthenticationMethod = machineEditor.MachineConnection.AuthenticationMethod;

            var startup = await startupService.RecoverDeploymentsAsync(Settings.IsFirstRunCompleted);
            if (startup.RecoveryError is not null)
            {
                PublishNotification(startup.RecoveryError, isError: true);
            }
            else if (startup.RecoveredDeploymentCount > 0)
            {
                PublishNotification(LocalizeFormat(Texts.InterruptedOperationsRecovered, startup.RecoveredDeploymentCount), isError: false);
            }

            await gitHubAccount.LoadAsync(Settings.GitHub);
        }

        public Task SaveAsync() => settingsService.SaveAsync(Settings);

        public async Task MarkProfileVerifiedAsync()
        {
            Settings.IsFirstRunCompleted = true;
            await SaveAsync();
        }

        public void UpdateFirstRunRequirement() => IsFirstRunSetupRequired = !machineEditor.IsRemoteMachineConfigured;

        public void RequireFirstRunSetup()
        {
            IsFirstRunSetupRequired = true;
            machineEditor.SettingsError = string.Empty;
            machineEditor.SettingsStatus = string.Empty;
        }

        partial void OnDeploymentNotificationsEnabledChanged(bool value)
        {
            Settings.DeploymentNotificationsEnabled = value;
            if (!_loadingNotificationPreference)
            {
                _ = SaveNotificationPreferenceAsync(value);
            }
        }

        private async Task SaveNotificationPreferenceAsync(bool enabled)
        {
            try
            {
                await settingsService.SaveDeploymentNotificationsEnabledAsync(enabled);
            }
            catch (Exception exception)
            {
                PublishNotification(exception.Message, isError: true);
            }
        }
    }
}
