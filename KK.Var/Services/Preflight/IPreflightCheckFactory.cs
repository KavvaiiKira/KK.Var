using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IPreflightCheckFactory
    {
        DeploymentPreflightCheck Success(string key, string message, string? detail = null);

        DeploymentPreflightCheck Warning(string key, string message, string? detail = null);

        DeploymentPreflightCheck Error(string key, string message, string? detail = null);
    }
}
