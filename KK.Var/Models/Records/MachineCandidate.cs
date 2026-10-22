using System;
using KK.Var.Configuration;

namespace KK.Var.Models
{
    internal sealed record MachineCandidate(
        RemoteMachine Machine,
        RemoteMachineSettings Connection,
        DateTime? ExpectedUpdatedAt,
        RemoteConnectionCheckResult Result);
}
