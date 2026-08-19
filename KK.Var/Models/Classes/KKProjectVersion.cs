using System;
using System.Collections.Generic;

namespace KK.Var.Models;

public sealed class KKProjectVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid KKProjectId { get; set; }

    public string Tag { get; set; } = string.Empty;

    public string ArtifactRelativePath { get; set; } = string.Empty;

    public string ArtifactSha256 { get; set; } = string.Empty;

    public long ArtifactSize { get; set; }

    public string? SourceCommitSha { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? Description { get; set; }

    public KKProject Project { get; set; } = null!;

    public ICollection<KKProjectDeployment> Deployments { get; set; }
        = new List<KKProjectDeployment>();
}
