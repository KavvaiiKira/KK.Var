namespace KK.Var.Models;

public sealed record GitHubRepository(
    long Id,
    string Name,
    string FullName,
    string CloneUrl,
    string DefaultBranch,
    bool IsPrivate);
