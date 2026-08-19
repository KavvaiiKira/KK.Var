using System;

namespace KK.Var.Models;

public sealed class KKProjectEnvironmentVariable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid KKProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public KKProject Project { get; set; } = null!;
}
