using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services
{
    public interface IBuildProcessRunner
    {
        Task RunProcessAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string?>? environment,
            CancellationToken cancellationToken);
    }
}
