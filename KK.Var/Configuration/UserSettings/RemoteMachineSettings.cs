using System.Text.Json.Serialization;

namespace KK.Var.Configuration;

public sealed class RemoteMachineSettings
{
    public string? Host { get; set; }

    public int Port { get; set; } = 22;

    public string? UserName { get; set; }

    public string AuthenticationMethod { get; set; } = "SSH-ключ";

    public string? PrivateKeyPath { get; set; }

    [JsonIgnore]
    public string? Password { get; set; }

    public string? HostKeyFingerprint { get; set; }

    public string? HostKeyHost { get; set; }

    public int? HostKeyPort { get; set; }

    public string? Architecture { get; set; }
}
