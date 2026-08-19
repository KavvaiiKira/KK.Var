namespace KK.Var.Models;

public sealed record GitHubUser(
    long Id,
    string Login,
    string? AvatarUrl);
