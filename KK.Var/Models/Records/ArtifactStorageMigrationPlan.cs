namespace KK.Var.Models;

public sealed record ArtifactStorageMigrationPlan(
    string SourceRoot,
    string TargetRoot,
    int FileCount,
    long TotalBytes,
    int ExistingVerifiedFiles);
