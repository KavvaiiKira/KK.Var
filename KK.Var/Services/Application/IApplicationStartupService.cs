using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IApplicationStartupService
    {
        Task<ApplicationStartupResult> RecoverDeploymentsAsync(bool isFirstRunCompleted);
    }
}
