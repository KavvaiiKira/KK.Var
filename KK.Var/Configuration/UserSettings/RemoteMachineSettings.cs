using System.Text.Json.Serialization;
using KK.Var.Enums;

namespace KK.Var.Configuration
{
    public sealed class RemoteMachineSettings
    {
        [JsonIgnore]
        public bool CleanDeployment { get; set; }

        public string? Host { get; set; }

        public int Port { get; set; } = 22;

        public string? UserName { get; set; }

        public SshAuthenticationMethod AuthenticationMethod { get; set; } = SshAuthenticationMethod.PrivateKey;

        public string? PrivateKeyPath { get; set; }

        [JsonIgnore]
        public string? Password { get; set; }

        public string? HostKeyFingerprint { get; set; }

        public string? HostKeyHost { get; set; }

        public int? HostKeyPort { get; set; }

        public string? Architecture { get; set; }
    }
}
