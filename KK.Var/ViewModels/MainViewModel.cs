using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string PrivateKeyAuthentication = "SSH-ключ";
    private const string PasswordAuthentication = "Пароль";

    private readonly IUserSettingsService? _userSettingsService;
    private readonly IRemoteConnectionService? _remoteConnectionService;
    private readonly IGitHubService? _gitHubService;
    private readonly IGitHubTokenStore? _gitHubTokenStore;
    private CancellationTokenSource? _gitHubAuthorizationCancellation;

    public MainViewModel()
    {
    }

    public MainViewModel(
        IUserSettingsService userSettingsService,
        IRemoteConnectionService remoteConnectionService,
        IGitHubService gitHubService,
        IGitHubTokenStore gitHubTokenStore)
    {
        _userSettingsService = userSettingsService;
        _remoteConnectionService = remoteConnectionService;
        _gitHubService = gitHubService;
        _gitHubTokenStore = gitHubTokenStore;
    }

    [ObservableProperty]
    public partial UserSettings Settings { get; set; } = new();

    [ObservableProperty]
    public partial string SettingsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SettingsError { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFirstRunSetupRequired { get; set; }

    [ObservableProperty]
    public partial bool IsConnectionCheckRunning { get; set; }

    [ObservableProperty]
    public partial string RemoteMachineArchitecture { get; set; } =
        "Будет определена автоматически при проверке подключения";

    [ObservableProperty]
    public partial bool IsGitHubConnected { get; set; }

    [ObservableProperty]
    public partial bool IsGitHubAuthorizationPending { get; set; }

    [ObservableProperty]
    public partial string GitHubAccountDisplay { get; set; } = "Не подключён";

    [ObservableProperty]
    public partial string GitHubConnectionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitHubUserCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GitHubVerificationUrl { get; set; } =
        "https://github.com/login/device";

    public IReadOnlyList<string> AuthenticationMethods { get; } =
        [PrivateKeyAuthentication, PasswordAuthentication];

    [ObservableProperty]
    public partial string AuthenticationMethod { get; set; } =
        PrivateKeyAuthentication;

    public bool IsPrivateKeyAuthentication =>
        AuthenticationMethod == PrivateKeyAuthentication;

    public bool IsPasswordAuthentication =>
        AuthenticationMethod == PasswordAuthentication;

    public bool IsRemoteMachineConfigured =>
        Settings.IsFirstRunCompleted &&
        string.IsNullOrEmpty(ValidateRemoteMachineSettings());

    public async Task LoadSettingsAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        Settings = await _userSettingsService.LoadAsync();
        RemoteMachineArchitecture = FormatArchitecture(
            Settings.RemoteMachine.Architecture);
        AuthenticationMethod = Settings.RemoteMachine.AuthenticationMethod == PasswordAuthentication
            ? PasswordAuthentication
            : PrivateKeyAuthentication;

        GitHubAccountDisplay = string.IsNullOrWhiteSpace(Settings.GitHub.AccountLogin)
            ? "Не подключён"
            : Settings.GitHub.AccountLogin;

        if (_gitHubTokenStore is not null)
        {
            try
            {
                var token = await _gitHubTokenStore.LoadAsync();
                IsGitHubConnected =
                    !string.IsNullOrWhiteSpace(token) &&
                    !string.IsNullOrWhiteSpace(Settings.GitHub.AccountLogin);
            }
            catch (Exception exception)
            {
                GitHubConnectionStatus = exception.Message;
                IsGitHubConnected = false;
            }
        }
    }

    [RelayCommand]
    private async Task ConnectGitHubAsync()
    {
        if (_gitHubService is null ||
            _gitHubTokenStore is null ||
            _userSettingsService is null ||
            IsGitHubAuthorizationPending)
        {
            return;
        }

        _gitHubAuthorizationCancellation?.Dispose();
        _gitHubAuthorizationCancellation = new CancellationTokenSource();
        var cancellationToken = _gitHubAuthorizationCancellation.Token;
        IsGitHubAuthorizationPending = true;
        GitHubConnectionStatus = "Получаем код авторизации...";

        try
        {
            var authorization = await _gitHubService.StartDeviceAuthorizationAsync(
                cancellationToken);
            GitHubUserCode = authorization.UserCode;
            GitHubVerificationUrl = authorization.VerificationUri.ToString();
            GitHubConnectionStatus =
                "Введите этот код на открывшейся странице GitHub";
            OpenBrowser(authorization.VerificationUri);

            var token = await _gitHubService.WaitForAccessTokenAsync(
                authorization,
                cancellationToken);
            var user = await _gitHubService.GetCurrentUserAsync(
                token,
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

            Settings.GitHub.AccountLogin = user.Login;
            Settings.GitHub.ConnectedAtUtc = DateTimeOffset.UtcNow;
            GitHubAccountDisplay = user.Login;
            GitHubConnectionStatus = "GitHub успешно подключён";
            IsGitHubConnected = true;
        }
        catch (OperationCanceledException)
        {
            GitHubConnectionStatus = "Подключение GitHub отменено";
        }
        catch (Exception exception)
        {
            GitHubConnectionStatus = exception.Message;
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
    private void CancelGitHubAuthorization()
    {
        _gitHubAuthorizationCancellation?.Cancel();
    }

    [RelayCommand]
    private void OpenGitHubVerificationPage()
    {
        OpenBrowser(new Uri(GitHubVerificationUrl));
    }

    [RelayCommand]
    private async Task DisconnectGitHubAsync()
    {
        if (_gitHubTokenStore is null || _userSettingsService is null)
        {
            return;
        }

        _gitHubAuthorizationCancellation?.Cancel();
        await _gitHubTokenStore.DeleteAsync();
        await _userSettingsService.ClearGitHubConnectionAsync();

        Settings.GitHub = new GitHubSettings();
        GitHubAccountDisplay = "Не подключён";
        GitHubConnectionStatus = "GitHub отключён";
        IsGitHubConnected = false;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        SettingsError = ValidateRemoteMachineSettings();

        if (!string.IsNullOrEmpty(SettingsError))
        {
            SettingsStatus = string.Empty;
            return;
        }

        Settings.IsFirstRunCompleted = true;
        await _userSettingsService.SaveAsync(Settings);
        IsFirstRunSetupRequired = false;
        SettingsStatus = "Настройки сохранены";
    }

    public void RequireFirstRunSetup()
    {
        IsFirstRunSetupRequired = true;
        SettingsError = string.Empty;
        SettingsStatus = string.Empty;
    }

    [RelayCommand]
    private async Task CheckRemoteConnectionAsync()
    {
        if (_remoteConnectionService is null ||
            _userSettingsService is null ||
            IsConnectionCheckRunning)
        {
            return;
        }

        SettingsError = ValidateRemoteMachineSettings();
        SettingsStatus = string.Empty;

        if (!string.IsNullOrEmpty(SettingsError))
        {
            return;
        }

        IsConnectionCheckRunning = true;
        RemoteMachineArchitecture = "Подключение и определение архитектуры...";

        try
        {
            var result = await _remoteConnectionService.CheckAsync(Settings.RemoteMachine);

            if (!result.IsSuccessful)
            {
                SettingsError = result.ErrorMessage;
                RemoteMachineArchitecture = FormatArchitecture(
                    Settings.RemoteMachine.Architecture);
                return;
            }

            Settings.RemoteMachine.Architecture = result.Architecture;
            await _userSettingsService.SaveRemoteMachineArchitectureAsync(
                result.Architecture);
            SettingsError = string.Empty;
            SettingsStatus = "Подключение успешно";
            RemoteMachineArchitecture = FormatArchitecture(result.Architecture);
        }
        finally
        {
            IsConnectionCheckRunning = false;
        }
    }

    public void ClearSettingsStatus()
    {
        SettingsStatus = string.Empty;
    }

    partial void OnAuthenticationMethodChanged(string value)
    {
        Settings.RemoteMachine.AuthenticationMethod = value;
        OnPropertyChanged(nameof(IsPrivateKeyAuthentication));
        OnPropertyChanged(nameof(IsPasswordAuthentication));
    }

    private string ValidateRemoteMachineSettings()
    {
        if (string.IsNullOrWhiteSpace(Settings.RemoteMachine.Host))
        {
            return "Укажите адрес удалённой машины.";
        }

        if (Settings.RemoteMachine.Port is < 1 or > 65535)
        {
            return "SSH-порт должен находиться в диапазоне от 1 до 65535.";
        }

        if (string.IsNullOrWhiteSpace(Settings.RemoteMachine.UserName))
        {
            return "Укажите пользователя SSH.";
        }

        if (IsPrivateKeyAuthentication &&
            string.IsNullOrWhiteSpace(Settings.RemoteMachine.PrivateKeyPath))
        {
            return "Укажите путь к приватному SSH-ключу.";
        }

        if (IsPasswordAuthentication &&
            string.IsNullOrWhiteSpace(Settings.RemoteMachine.Password))
        {
            return "Укажите пароль SSH.";
        }

        return string.Empty;
    }

    private static string FormatArchitecture(string? architecture) =>
        string.IsNullOrWhiteSpace(architecture)
            ? "Будет определена автоматически при проверке подключения"
            : $"Определена автоматически: {architecture}";

    private static void OpenBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true,
        });
    }
}
