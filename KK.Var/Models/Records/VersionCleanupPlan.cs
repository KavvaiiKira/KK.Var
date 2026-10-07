using System;
using System.Collections.Generic;

namespace KK.Var.Models;

public sealed record VersionCleanupPlan(
    Guid ProjectId,
    string ArtifactRoot,
    IReadOnlyList<VersionCleanupItem> Items,
    long BytesToDelete)
{
    public int VersionCount => Items.Count;
}
