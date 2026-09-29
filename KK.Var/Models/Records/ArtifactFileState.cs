namespace KK.Var.Models;

public sealed record ArtifactFileState(
    string AbsolutePath,
    bool Exists,
    long Size);
