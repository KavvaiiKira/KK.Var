using KK.Var.Constants;

namespace KK.Var.Configuration
{
    public sealed class DatabaseOptions
    {
        public const string SectionName = "Database";

        public string FileName { get; init; } = ApplicationConstants.DefaultDatabaseFileName;
    }
}
