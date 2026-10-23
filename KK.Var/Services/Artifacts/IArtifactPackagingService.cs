using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IArtifactPackagingService
    {
        Task CreateTarGzAsync(
            string sourceDirectory,
            Stream destination,
            CancellationToken cancellationToken);

        Task<string> CalculateSha256Async(
            string path,
            CancellationToken cancellationToken);
    }
}
