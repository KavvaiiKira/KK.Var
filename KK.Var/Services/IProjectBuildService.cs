using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IProjectBuildService
    {
        Task BuildAsync(
            ProjectBuildProvider provider,
            KKProject project,
            ProjectBuildConfiguration configuration,
            string sourceDirectory,
            string outputDirectory,
            string remoteArchitecture,
            CancellationToken cancellationToken);
    }
}
