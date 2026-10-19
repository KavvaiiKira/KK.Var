using System;

namespace KK.Var.Models;

public sealed record DeploymentRequest(
    Guid ProjectId,
    string VersionTag,
    string? Description,
    string? GitHubBranch = null,
    string? SourceCommitSha = null,
    Guid? RemoteMachineId = null);
