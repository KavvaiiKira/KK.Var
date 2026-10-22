using KK.Var.Configuration;
using KK.Var.Enums;
using Renci.SshNet;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteClientFactory : IRemoteClientFactory
    {
        public SshClient CreateSshClient(RemoteMachineSettings settings) =>
            settings.AuthenticationMethod == SshAuthenticationMethod.Password ?
                new SshClient(settings.Host!, settings.Port, settings.UserName!, settings.Password!) :
                new SshClient(
                    settings.Host!,
                    settings.Port,
                    settings.UserName!,
                    new PrivateKeyFile(settings.PrivateKeyPath!));

        public SftpClient CreateSftpClient(RemoteMachineSettings settings) =>
            settings.AuthenticationMethod == SshAuthenticationMethod.Password ?
                new SftpClient(settings.Host!, settings.Port, settings.UserName!, settings.Password!) :
                new SftpClient(
                    settings.Host!,
                    settings.Port,
                    settings.UserName!,
                    new PrivateKeyFile(settings.PrivateKeyPath!));
    }
}
