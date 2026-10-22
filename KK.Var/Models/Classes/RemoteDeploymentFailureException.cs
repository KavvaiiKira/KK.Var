using System;

namespace KK.Var.Models
{
    public sealed class RemoteDeploymentFailureException(
        string message,
        Exception innerException,
        bool automaticRollbackCompleted) : Exception(message, innerException)
    {
        public bool AutomaticRollbackCompleted { get; } = automaticRollbackCompleted;
    }
}
