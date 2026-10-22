namespace KK.Var.Constants
{
    internal static class DatabaseSchema
    {
        public const string ProjectsTable = "KKProjects";

        public const string DeploymentsTable = "KKProjectDeployments";

        public const string EnvironmentVariablesTable = "KKProjectEnvironmentVariables";

        public const string VersionsTable = "KKProjectVersions";

        public const string CaseInsensitiveCollation = "NOCASE";

        public const string ProjectSourceConstraint = "CK_KKProjects_Source";

        public const string MaxVersionsConstraint = "CK_KKProjects_MaxStoredVersions";

        public const string MaxVersionBytesConstraint = "CK_KKProjects_MaxStoredVersionBytes";

        public const string ArtifactSizeConstraint = "CK_KKProjectVersions_ArtifactSize";

        public const string PositiveMaxVersionsCheck = "MaxStoredVersions > 0";

        public const string PositiveMaxVersionBytesCheck = "MaxStoredVersionBytes IS NULL OR MaxStoredVersionBytes > 0";

        public const string NonnegativeArtifactSizeCheck = "ArtifactSize >= 0";
    }
}
