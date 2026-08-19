namespace KK.Var.Configuration;

public sealed class RemoteMachineSettings
{
    public string? Host { get; set; }

    public int Port { get; set; } = 22;

    public string? UserName { get; set; }

    public string AuthenticationMethod { get; set; } = "SSH-ключ";

    public string? PrivateKeyPath { get; set; }

    public string? Password { get; set; }

    public string? Architecture { get; set; }
}
