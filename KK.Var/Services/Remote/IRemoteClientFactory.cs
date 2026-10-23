using KK.Var.Configuration;
using Renci.SshNet;

namespace KK.Var.Services
{
    public interface IRemoteClientFactory
    {
        SshClient CreateSshClient(RemoteMachineSettings settings);

        SftpClient CreateSftpClient(RemoteMachineSettings settings);
    }
}
