using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IRemoteDeploymentValidator
    {
        void Validate(
            RemoteMachineSettings settings,
            KKProject project,
            string artifactPath);

        void ValidateConnection(
            RemoteMachineSettings settings,
            KKProject project);

        void ValidateOperationId(string operationId);

        void RejectControlCharacters(string value);
    }
}
