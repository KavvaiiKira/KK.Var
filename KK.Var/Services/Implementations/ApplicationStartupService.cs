using System;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class ApplicationStartupService(
        IDeploymentRecoveryService recoveryService,
        ILocalizationService localizationService) : IApplicationStartupService
    {
        public async Task<ApplicationStartupResult> RecoverDeploymentsAsync(bool isFirstRunCompleted)
        {
            if (!isFirstRunCompleted)
            {
                return new ApplicationStartupResult(0, null);
            }

            try
            {
                return new ApplicationStartupResult(await recoveryService.RecoverInterruptedAsync(), null);
            }
            catch (Exception exception)
            {
                return new ApplicationStartupResult(0, localizationService.Format(
                    "Не удалось восстановить прерванный Deploy: {0}",
                    exception.Message));
            }
        }
    }
}
