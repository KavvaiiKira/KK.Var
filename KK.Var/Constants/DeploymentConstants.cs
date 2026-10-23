namespace KK.Var.Constants
{
    internal static class DeploymentConstants
    {
        public const string MarkerRelativePath = "/.kk-var-deployment";

        public const string RemoteTemporaryPrefix = "/tmp/kk-var-";

        public const string ServiceExtension = ".service";

        public const string ServiceNameValidationPattern = "^[A-Za-z0-9_.@-]+\\.service$";

        public const string ServiceBackupExtension = ".service.backup";

        public const string SystemdDirectory = "/etc/systemd/system/";
    }
}
