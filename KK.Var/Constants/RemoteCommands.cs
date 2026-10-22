namespace KK.Var.Constants
{
    internal static class RemoteCommands
    {
        public const string Architecture = "uname -m";

        public const string CheckSudo = "sudo -n true";

        public const string CheckPython = "command -v python3 >/dev/null";

        public const string ReloadSystemd = "sudo systemctl daemon-reload";

        public const string CheckDeploymentTools = "command -v systemctl >/dev/null && command -v tar >/dev/null";

        public const string CheckRunningSystemd = "command -v systemctl >/dev/null && test -d /run/systemd/system && command -v tar >/dev/null";
    }
}
