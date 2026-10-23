namespace KK.Var.Services
{
    public static class RemoteShell
    {
        public static string Q(string value) =>
            $"'{value.Replace("'", "'\"'\"'")}'";

        public static string UnitQuote(string value) =>
            $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

        public static string UnitPath(string value) => value
            .Replace("\\", "\\\\")
            .Replace(" ", "\\x20")
            .Replace("\t", "\\t")
            .Replace("%", "%%");
    }
}
