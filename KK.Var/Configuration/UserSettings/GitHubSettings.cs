using System;

namespace KK.Var.Configuration;

public sealed class GitHubSettings
{
    public string? AccountLogin { get; set; }

    public DateTimeOffset? ConnectedAtUtc { get; set; }
}
