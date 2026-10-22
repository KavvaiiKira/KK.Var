using System;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class ProjectHealthCheckEditorViewModel(ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        [ObservableProperty]
        public partial ProjectHealthCheckType SelectedHealthCheckType { get; set; } = ProjectHealthCheckType.None;

        [ObservableProperty]
        public partial int HealthCheckTimeoutSeconds { get; set; } = 30;

        [ObservableProperty]
        public partial int HealthCheckIntervalSeconds { get; set; } = 5;

        [ObservableProperty]
        public partial int HealthCheckAttempts { get; set; } = 3;

        [ObservableProperty]
        public partial int HealthCheckStabilityDelaySeconds { get; set; } = 10;

        [ObservableProperty]
        public partial string HealthCheckHttpUrl { get; set; } = string.Empty;

        [ObservableProperty]
        public partial int HealthCheckTcpPort { get; set; } = 8080;

        [ObservableProperty]
        public partial string HealthCheckCommand { get; set; } = string.Empty;

        public bool IsHealthCheckEnabled =>
            SelectedHealthCheckType != ProjectHealthCheckType.None;

        public bool IsStabilityDelayHealthCheck =>
            SelectedHealthCheckType == ProjectHealthCheckType.StabilityDelay;

        public bool IsHttpHealthCheck =>
            SelectedHealthCheckType == ProjectHealthCheckType.Http;

        public bool IsTcpHealthCheck =>
            SelectedHealthCheckType == ProjectHealthCheckType.Tcp;

        public bool IsCommandHealthCheck =>
            SelectedHealthCheckType == ProjectHealthCheckType.Command;

        public bool HasHealthCheckRetrySettings =>
            SelectedHealthCheckType is ProjectHealthCheckType.Http or
                ProjectHealthCheckType.Tcp or
                ProjectHealthCheckType.Command;

        partial void OnSelectedHealthCheckTypeChanged(ProjectHealthCheckType value)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(IsHealthCheckEnabled));
            OnPropertyChanged(nameof(IsStabilityDelayHealthCheck));
            OnPropertyChanged(nameof(IsHttpHealthCheck));
            OnPropertyChanged(nameof(IsTcpHealthCheck));
            OnPropertyChanged(nameof(IsCommandHealthCheck));
            OnPropertyChanged(nameof(HasHealthCheckRetrySettings));
        }

        partial void OnHealthCheckTimeoutSecondsChanged(int value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnHealthCheckIntervalSecondsChanged(int value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnHealthCheckAttemptsChanged(int value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnHealthCheckStabilityDelaySecondsChanged(int value) => Changed?.Invoke(
            this,
            EventArgs.Empty);

        partial void OnHealthCheckHttpUrlChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnHealthCheckTcpPortChanged(int value) => Changed?.Invoke(this, EventArgs.Empty);

        partial void OnHealthCheckCommandChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

        public string ValidateHealthCheck()
        {
            if (IsStabilityDelayHealthCheck &&
                HealthCheckStabilityDelaySeconds is < 1 or > 3600)
            {
                return Localize(
                    "Задержка стабильности должна быть от 1 до 3600 секунд.");
            }

            if (!HasHealthCheckRetrySettings)
            {
                return string.Empty;
            }

            if (HealthCheckTimeoutSeconds is < 1 or > 3600)
            {
                return Localize(
                    "Timeout health-check должен быть от 1 до 3600 секунд.");
            }

            if (HealthCheckIntervalSeconds is < 1 or > 3600)
            {
                return Localize(
                    "Интервал health-check должен быть от 1 до 3600 секунд.");
            }

            if (HealthCheckAttempts is < 1 or > 100)
            {
                return Localize(
                    "Количество попыток health-check должно быть от 1 до 100.");
            }

            if (IsHttpHealthCheck &&
                (!Uri.TryCreate(HealthCheckHttpUrl.Trim(), UriKind.Absolute, out var uri) ||
                    (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))))
            {
                return Localize(
                    "Укажите абсолютный HTTP- или HTTPS-адрес health-check.");
            }

            if (IsTcpHealthCheck && HealthCheckTcpPort is < 1 or > 65535)
            {
                return Localize("TCP-порт должен быть от 1 до 65535.");
            }

            if (IsCommandHealthCheck && string.IsNullOrWhiteSpace(HealthCheckCommand))
            {
                return Localize("Укажите команду health-check.");
            }

            return string.Empty;
        }

        public event EventHandler? Changed;
    }
}
