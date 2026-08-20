using System;

namespace KK.Var.Models;

public sealed record GitHubToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    DateTimeOffset? RefreshTokenExpiresAtUtc);
