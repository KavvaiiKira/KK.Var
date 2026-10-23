using System;
using System.Collections.Generic;
using System.Text.Json;
using KK.Var.Enums;

namespace KK.Var.Models
{
    public sealed record DeploymentRecoveryAttempt(
        Guid Id,
        DateTime StartedAtUtc,
        DateTime? CompletedAtUtc,
        DeploymentRecoveryAttemptStatus Status,
        DeploymentRecoveryOutcome? Outcome,
        string LogPath,
        string? ErrorMessage)
    {
        public static IReadOnlyList<DeploymentRecoveryAttempt> Read(string json) =>
            JsonSerializer.Deserialize<List<DeploymentRecoveryAttempt>>(json) ?? [];
    }
}
