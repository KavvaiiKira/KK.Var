using System;
using System.Collections.Generic;

namespace KK.Var.Models;

public sealed class RemoteMachine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string UserName { get; set; } = string.Empty;
    public string AuthenticationMethod { get; set; } = "SSH-ключ";
    public string? PrivateKeyPath { get; set; }

    public string? HostKeyFingerprint { get; set; }

    public string? Architecture { get; set; }

    public bool IsVerified { get; set; }

    public bool IsArchived { get; set; }

    public int ServerGeneration { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAtUtc { get; set; }

    public List<KKProject> Projects { get; set; } = [];
    public List<KKProjectDeployment> Deployments { get; set; } = [];
}
