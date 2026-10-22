using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class PreflightCheckFactory(ILocalizationService localizationService) : IPreflightCheckFactory
    {
        public DeploymentPreflightCheck Success(string key, string message, string? detail = null) =>
            new DeploymentPreflightCheck(
                key,
                PreflightStatus.Success,
                localizationService.Get(message),
                detail);

        public DeploymentPreflightCheck Warning(string key, string message, string? detail = null) =>
            new DeploymentPreflightCheck(
                key,
                PreflightStatus.Warning,
                localizationService.Get(message),
                detail);

        public DeploymentPreflightCheck Error(string key, string message, string? detail = null) =>
            new DeploymentPreflightCheck(
                key,
                PreflightStatus.Error,
                localizationService.Get(message),
                detail);
    }
}
