using System;
using KK.Var.Enums;

namespace KK.Var.Models;

public sealed record DeploymentCompletionNotice(
    Guid ProjectId,
    Guid? DeploymentId,
    string ProjectName,
    DeploymentStatus Status,
    DeploymentOperationType OperationType,
    bool AutomaticRollbackCompleted = false,
    bool AutomaticRollbackFailed = false);
