namespace KK.Var.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string FileName { get; init; } = "kk-var.db";
}
