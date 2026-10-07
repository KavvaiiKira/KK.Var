using System;

namespace KK.Var.Models;

public sealed record VersionCleanupItem(
    Guid VersionId,
    string ArtifactRelativePath,
    long ArtifactBytes);
