namespace KK.Var.Configuration;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public string ClientId { get; set; } = string.Empty;

    public string Scope { get; set; } = "repo read:user";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        ClientId != "PASTE_YOUR_GITHUB_CLIENT_ID_HERE";
}
