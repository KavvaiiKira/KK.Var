namespace KK.Var.Configuration;

public sealed class UserSettings
{
    public string Theme { get; set; } = "System";

    public AccessSettings Access { get; set; } = new();
}

public sealed class AccessSettings
{
    public string? Endpoint { get; set; }

    public string? UserName { get; set; }

    // Passwords, API keys and tokens must be stored in an OS-backed secret store,
    // not in this plain JSON settings file.
}
