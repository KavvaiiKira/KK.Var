namespace KK.Var.Constants
{
    internal static class ArtifactConstants
    {
        public const string ArchiveExtension = ".tar.gz";

        public const string VersionTagPattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$";

        public const string ArtifactRelativePathPattern = "\\A[0-9a-f]{32}/[A-Za-z0-9][A-Za-z0-9._-]{0,199}\\.tar\\.gz\\z";
    }
}
