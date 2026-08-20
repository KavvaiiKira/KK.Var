using KK.Var.Enums;

namespace KK.Var.Models;

public sealed record ProjectArtifact(
    string AbsolutePath,
    string RelativePath,
    string Sha256,
    long Size,
    string? SourceCommitSha,
    ProjectBuildProvider BuildProvider);
