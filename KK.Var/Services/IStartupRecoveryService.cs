using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;

namespace KK.Var.Services;

public interface IStartupRecoveryService
{
    Task<string> BackupAndResetAsync(
        StartupRecoveryKind kind,
        CancellationToken cancellationToken = default);

    void OpenUserDataDirectory();
}
