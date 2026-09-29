namespace KK.Var.Models;

public sealed record ArtifactStorageMigrationResult(
    ArtifactStorageMigrationPlan Plan,
    string? Warning);
