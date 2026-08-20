using System.Threading;
using System.Threading.Tasks;

namespace KK.Var.Services;

public interface ISshPasswordStore
{
    Task SaveAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        CancellationToken cancellationToken = default);
}
