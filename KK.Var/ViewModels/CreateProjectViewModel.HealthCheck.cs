using KK.Var.Enums;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        public ProjectHealthCheckType SelectedHealthCheckType
        {
            get => HealthCheckEditor.SelectedHealthCheckType;
            set => HealthCheckEditor.SelectedHealthCheckType = value;
        }

        public int HealthCheckTimeoutSeconds
        {
            get => HealthCheckEditor.HealthCheckTimeoutSeconds;
            set => HealthCheckEditor.HealthCheckTimeoutSeconds = value;
        }

        public int HealthCheckIntervalSeconds
        {
            get => HealthCheckEditor.HealthCheckIntervalSeconds;
            set => HealthCheckEditor.HealthCheckIntervalSeconds = value;
        }

        public int HealthCheckAttempts
        {
            get => HealthCheckEditor.HealthCheckAttempts;
            set => HealthCheckEditor.HealthCheckAttempts = value;
        }

        public int HealthCheckStabilityDelaySeconds
        {
            get => HealthCheckEditor.HealthCheckStabilityDelaySeconds;
            set => HealthCheckEditor.HealthCheckStabilityDelaySeconds = value;
        }

        public string HealthCheckHttpUrl
        {
            get => HealthCheckEditor.HealthCheckHttpUrl;
            set => HealthCheckEditor.HealthCheckHttpUrl = value;
        }

        public int HealthCheckTcpPort
        {
            get => HealthCheckEditor.HealthCheckTcpPort;
            set => HealthCheckEditor.HealthCheckTcpPort = value;
        }

        public string HealthCheckCommand
        {
            get => HealthCheckEditor.HealthCheckCommand;
            set => HealthCheckEditor.HealthCheckCommand = value;
        }

        public bool IsHealthCheckEnabled => HealthCheckEditor.IsHealthCheckEnabled;

        public bool IsStabilityDelayHealthCheck => HealthCheckEditor.IsStabilityDelayHealthCheck;

        public bool IsHttpHealthCheck => HealthCheckEditor.IsHttpHealthCheck;

        public bool IsTcpHealthCheck => HealthCheckEditor.IsTcpHealthCheck;

        public bool IsCommandHealthCheck => HealthCheckEditor.IsCommandHealthCheck;

        public bool HasHealthCheckRetrySettings => HealthCheckEditor.HasHealthCheckRetrySettings;

        public ProjectHealthCheckEditorViewModel HealthCheckEditor { get; }

        private string ValidateHealthCheck() => HealthCheckEditor.ValidateHealthCheck();
    }
}
