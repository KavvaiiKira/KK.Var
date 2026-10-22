using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IProjectSourceService
    {
        Task<string?> AcquireSourceAsync(
            KKProject project,
            string destination,
            string? sourceCommitSha,
            CancellationToken cancellationToken);
    }
}
