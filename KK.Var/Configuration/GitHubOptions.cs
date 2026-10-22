using KK.Var.Constants;

namespace KK.Var.Configuration
{
    public sealed class GitHubOptions
    {
        public const string SectionName = "GitHub";

        public string ClientId { get; set; } = string.Empty;

        public string Scope { get; set; } = GitHubProtocol.DefaultScope;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ClientId) &&
            ClientId != GitHubProtocol.ClientIdPlaceholder;
    }
}
