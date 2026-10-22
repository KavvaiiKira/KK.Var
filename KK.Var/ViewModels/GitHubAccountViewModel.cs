using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Constants;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class GitHubAccountViewModel(
        IGitHubService _gitHubService,
        IGitHubTokenStore _gitHubTokenStore,
        IGitHubAuthenticationService _authentication,
        IUserSettingsService _userSettingsService,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private CancellationTokenSource? _gitHubAuthorizationCancellation;

        [ObservableProperty]
        public partial bool IsGitHubConnected { get; private set; }

        [ObservableProperty]
        public partial bool IsGitHubAuthorizationPending { get; private set; }

        [ObservableProperty]
        public partial string GitHubAccountDisplay { get; set; } = localizationService.Get("Не подключён");

        [ObservableProperty]
        public partial string GitHubConnectionStatus { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string GitHubUserCode { get; private set; } = string.Empty;

        [ObservableProperty]
        public partial string GitHubVerificationUrl { get; private set; } = GitHubProtocol.VerificationPage;

        [RelayCommand]
        private async Task ConnectGitHubAsync()
        {
            if (IsGitHubAuthorizationPending)
            {
                return;
            }

            _gitHubAuthorizationCancellation?.Dispose();
            _gitHubAuthorizationCancellation = new CancellationTokenSource();

            var cancellationToken = _gitHubAuthorizationCancellation.Token;

            IsGitHubAuthorizationPending = true;
            GitHubConnectionStatus = Localize("Получаем код авторизации...");

            try
            {
                var authorization = await _gitHubService.StartDeviceAuthorizationAsync(cancellationToken);

                GitHubUserCode = authorization.UserCode;
                GitHubVerificationUrl = authorization.VerificationUri.ToString();
                GitHubConnectionStatus = Localize(
                    "Введите этот код на открывшейся странице GitHub");
                OpenBrowser(authorization.VerificationUri);

                var token = await _gitHubService.WaitForAccessTokenAsync(
                    authorization,
                    cancellationToken);

                var user = await _gitHubService.GetCurrentUserAsync(
                    token.AccessToken,
                    cancellationToken);

                await _gitHubTokenStore.SaveAsync(token, cancellationToken);

                try
                {
                    await _userSettingsService.SaveGitHubConnectionAsync(
                        user.Login,
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                }
                catch
                {
                    await _gitHubTokenStore.DeleteAsync(cancellationToken);

                    throw;
                }

                _settings.AccountLogin = user.Login;
                _settings.ConnectedAtUtc = DateTimeOffset.UtcNow;
                GitHubAccountDisplay = user.Login;
                GitHubConnectionStatus = Localize("GitHub успешно подключён");
                IsGitHubConnected = true;
                PublishNotification(Localize("GitHub успешно подключён"), isError: false);
            }
            catch (OperationCanceledException)
            {
                GitHubConnectionStatus = Localize("Подключение GitHub отменено");
            }
            catch (Exception exception)
            {
                GitHubConnectionStatus = Localize(exception.Message);
                PublishNotification(Localize(exception.Message), isError: true);
            }
            finally
            {
                IsGitHubAuthorizationPending = false;
                GitHubUserCode = string.Empty;
                _gitHubAuthorizationCancellation?.Dispose();
                _gitHubAuthorizationCancellation = null;
            }
        }

        [RelayCommand]
        private void CancelGitHubAuthorization() =>
            _gitHubAuthorizationCancellation?.Cancel();

        [RelayCommand]
        private void OpenGitHubVerificationPage() =>
            OpenBrowser(new Uri(GitHubVerificationUrl));

        [RelayCommand]
        private async Task DisconnectGitHubAsync()
        {

            _gitHubAuthorizationCancellation?.Cancel();

            await _gitHubTokenStore.DeleteAsync();
            await _userSettingsService.ClearGitHubConnectionAsync();

            _settings.AccountLogin = null;
            _settings.ConnectedAtUtc = null;
            GitHubAccountDisplay = Localize("Не подключён");
            GitHubConnectionStatus = Localize("GitHub отключён");
            IsGitHubConnected = false;

            PublishNotification(Localize("GitHub отключён"), isError: false);
        }

        private GitHubSettings _settings = new GitHubSettings();

        public async Task LoadAsync(GitHubSettings settings)
        {
            _settings = settings;
            GitHubAccountDisplay = string.IsNullOrWhiteSpace(settings.AccountLogin) ?
                Localize("Не подключён") :
                settings.AccountLogin;
            try
            {
                var token = await _authentication.GetTokenAsync();
                IsGitHubConnected = token is not null && !string.IsNullOrWhiteSpace(settings.AccountLogin);
            }
            catch (Exception exception)
            {
                GitHubConnectionStatus = Localize(exception.Message);
                IsGitHubConnected = false;
            }
        }

        private static void OpenBrowser(Uri uri) =>
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
