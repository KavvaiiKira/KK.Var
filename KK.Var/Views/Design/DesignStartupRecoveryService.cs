using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Services;

namespace KK.Var.Views.Design
{
    internal sealed class DesignStartupRecoveryService : IStartupRecoveryService
    {
        private const string RecoveryUnavailableMessage = "Recovery is unavailable in the designer.";

        private const string UserDataUnavailableMessage = "User data is unavailable in the designer.";

        public Task<string> BackupAndResetAsync(
            StartupRecoveryKind kind,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(RecoveryUnavailableMessage);

        public void OpenUserDataDirectory() =>
            throw new InvalidOperationException(UserDataUnavailableMessage);
    }
}
