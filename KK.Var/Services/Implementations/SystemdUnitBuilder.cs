using KK.Var.Constants;
using KK.Var.Models;
using static KK.Var.Services.ProjectRuntime;
using static KK.Var.Services.RemoteShell;

namespace KK.Var.Services.Implementations
{
    public sealed class SystemdUnitBuilder(IRemoteDeploymentValidator validator) : ISystemdUnitBuilder
    {
        private const string UnitSection = "[Unit]";

        private const string NetworkDependency = "After=network.target";

        private const string ServiceSection = "[Service]";

        private const string SimpleServiceType = "Type=simple";

        private const string RestartOnFailure = "Restart=on-failure";

        private const string RestartDelay = "RestartSec=5";

        private const string InstallSection = "[Install]";

        private const string MultiUserTarget = "WantedBy=multi-user.target";

        public string CreateUnit(KKProject project, string userName)
        {
            validator.RejectControlCharacters(userName);

            var directory = project.RemoteDeploymentDirectory.TrimEnd('/');
            var executable = $"{directory}/{GetExecutableRelativePath(project)}";

            var execStart = IsPythonProject(project) ?
                $"{UnitQuote(directory + BuildConstants.PythonInterpreterRelativePath)} {UnitQuote(executable)}" :
                UnitQuote(executable);

            return string.Join(
                '\n',
                [
                            UnitSection,
                            $"Description={project.EffectiveDescription.Replace("\r", " ").Replace("\n", " ")}",
                            NetworkDependency,
                            "",
                            ServiceSection,
                            SimpleServiceType,
                            $"User={userName}",
                            $"WorkingDirectory={UnitPath(directory)}",
                            $"ExecStart={execStart}",
                            RestartOnFailure,
                            RestartDelay,
                            "",
                            InstallSection,
                            MultiUserTarget,
                            "",
                        ]);
        }
    }
}
