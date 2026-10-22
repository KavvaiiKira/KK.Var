namespace KK.Var.Constants
{
    internal static class GitHubProtocol
    {
        public const string ClientIdField = "client_id";

        public const string ScopeField = "scope";

        public const string ExpiresInField = "expires_in";

        public const string IntervalField = "interval";

        public const string DeviceCodeField = "device_code";

        public const string UserCodeField = "user_code";

        public const string VerificationUriField = "verification_uri";

        public const string GrantTypeField = "grant_type";

        public const string AccessTokenField = "access_token";

        public const string ErrorField = "error";

        public const string ErrorDescriptionField = "error_description";

        public const string AuthorizationPendingError = "authorization_pending";

        public const string SlowDownError = "slow_down";

        public const string AccessDeniedError = "access_denied";

        public const string ExpiredTokenError = "expired_token";

        public const string DeviceFlowDisabledError = "device_flow_disabled";

        public const string IncorrectClientCredentialsError = "incorrect_client_credentials";

        public const string RefreshTokenField = "refresh_token";

        public const string RefreshTokenExpiresInField = "refresh_token_expires_in";

        public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

        public const string UserAgent = "KK.Var/1.0";

        public const string JsonMediaType = "application/json";

        public const string ApiMediaType = "application/vnd.github+json";

        public const string ApiVersionHeader = "X-GitHub-Api-Version";

        public const string ApiVersion = "2022-11-28";

        public const string BearerScheme = "Bearer";

        public const string LinkHeader = "Link";

        public const string DeviceCodeEndpoint = "https://github.com/login/device/code";

        public const string AccessTokenEndpoint = "https://github.com/login/oauth/access_token";

        public const string CurrentUserEndpoint = "https://api.github.com/user";

        public const string RepositoriesEndpoint = "https://api.github.com/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member";

        public const string VerificationPage = "https://github.com/login/device";

        public const string DefaultScope = "repo read:user";

        public const string ClientIdPlaceholder = "PASTE_YOUR_GITHUB_CLIENT_ID_HERE";

        public const string IdField = "id";

        public const string LoginField = "login";

        public const string AvatarUrlField = "avatar_url";

        public const string NameField = "name";

        public const string FullNameField = "full_name";

        public const string CloneUrlField = "clone_url";

        public const string PrivateField = "private";

        public const string DefaultBranchField = "default_branch";

        public const string CommitField = "commit";

        public const string AuthorField = "author";

        public const string MessageField = "message";

        public const string ShaField = "sha";

        public const string TreeField = "tree";

        public const string TypeField = "type";

        public const string PathField = "path";
        public const string RepositoryApiPrefix = "https://api.github.com/repos/";

        public const string BranchesPagePrefix = "/branches?per_page=100&page=";

        public const string BranchPathPrefix = "/branches/";

        public const string CommitPathPrefix = "/commits/";

        public const string ArchivePathPrefix = "/zipball/";

        public const string LatestCommitQuery = "/commits?per_page=1";

        public const string GitCommitPathPrefix = "/git/commits/";

        public const string GitTreePathPrefix = "/git/trees/";

        public const string RecursiveTreeQuery = "?recursive=1";

        public const string NextPageRelation = "rel=\"next\"";
    }
}
