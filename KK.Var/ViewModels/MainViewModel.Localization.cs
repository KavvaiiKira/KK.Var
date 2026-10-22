using System;
using System.Diagnostics;
using System.Threading.Tasks;
using KK.Var.Constants;
using KK.Var.Enums;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        public string LanguageButtonText =>
            _localizationService.CurrentLanguage == ApplicationLanguage.English ?
                UiConstants.EnglishLanguageCode :
                UiConstants.RussianLanguageCode;

        public string LanguageButtonToolTip =>
            _localizationService.CurrentLanguage == ApplicationLanguage.English ?
                Texts.SwitchRussian :
                Texts.SwitchEnglish;

        public async Task SwitchLanguageAsync()
        {
            var nextLanguage = _localizationService.CurrentLanguage == ApplicationLanguage.Russian ?
                ApplicationLanguage.English :
                ApplicationLanguage.Russian;

            _localizationService.SetLanguage(nextLanguage);
            DeploymentGitHubSource.RefreshLocalization();
            InvalidateDeploymentPreflight();

            AppSettings.Settings.Language = nextLanguage;

            MachineEditor.RefreshAuthenticationMethods();

            ProjectEditor.RefreshLocalization();

            RefreshLocalizedState();

            MachineEditor.SettingsStatus = string.Empty;
            MachineEditor.SettingsError = string.Empty;
            GitHubAccount.GitHubConnectionStatus = string.Empty;

            ClearStatusNotification();

            History.RefreshLocalization();

            ProjectList.RefreshLocalization();

            foreach (var item in History.HistoryItems)
            {
                item.RefreshLocalization();
            }

            foreach (var item in ProjectDetails.ProjectHistory)
            {
                item.RefreshLocalization();
            }

            foreach (var item in ProjectDetails.ProjectVersions)
            {
                item.RefreshLocalization();
            }

            SelectedDeploymentDetails?.RefreshLocalization();

            OnPropertyChanged(string.Empty);

            await AppSettings.SaveAsync();
        }

        public void OpenReadme()
        {
            var uri = AppSettings.Settings.Language == ApplicationLanguage.English ?
                new Uri(ApplicationConstants.EnglishReadmeUrl) :
                new Uri(ApplicationConstants.RepositoryUrl);

            OpenBrowser(uri);
        }

        public string Localize(string key) =>
            _localizationService.Get(key);

        public string LocalizeFormat(string key, params object?[] arguments) =>
            _localizationService.Format(key, arguments);

        private void RefreshLocalizedState()
        {
            EnvironmentEditor.RefreshLocalization();
            ArtifactStorage.RefreshLocalization();
            ProjectDetails.RefreshLocalization();
            MachineEditor.RefreshLocalization();
            DeploymentWorkspace.RefreshLocalization();

            MachineEditor.RemoteMachineArchitecture = MachineEditor.FormatArchitecture(MachineEditor.MachineConnection.Architecture);

            GitHubAccount.GitHubAccountDisplay =
                string.IsNullOrWhiteSpace(AppSettings.Settings.GitHub.AccountLogin) ?
                    Texts.NotConnected :
                    AppSettings.Settings.GitHub.AccountLogin;
        }

        private static void OpenBrowser(Uri uri) =>
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.ToString(),
                UseShellExecute = true,
            });
    }
}
