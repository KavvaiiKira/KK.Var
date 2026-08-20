using System;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace KK.Var.Services.Implementations;

internal sealed class SshHostKeyValidator(string? expectedFingerprint)
{
    public string ObservedFingerprint { get; private set; } = string.Empty;

    public bool RequiresConfirmation { get; private set; }

    public void Attach(BaseClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.HostKeyReceived += ClientOnHostKeyReceived;
    }

    private void ClientOnHostKeyReceived(object? sender, HostKeyEventArgs eventArgs)
    {
        ObservedFingerprint = eventArgs.FingerPrintSHA256;
        eventArgs.CanTrust = !string.IsNullOrWhiteSpace(expectedFingerprint) &&
            string.Equals(
                expectedFingerprint.Trim(),
                ObservedFingerprint,
                StringComparison.Ordinal);
        RequiresConfirmation = !eventArgs.CanTrust;
    }
}
