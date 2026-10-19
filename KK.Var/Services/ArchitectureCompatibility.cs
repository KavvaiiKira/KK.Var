namespace KK.Var.Services;

public static class ArchitectureCompatibility
{
    public static string Normalize(string? architecture) => architecture?.Trim().ToLowerInvariant() switch
    {
        "amd64" or "x64" or "x86_64" => "x86_64",
        "arm64" or "aarch64" => "aarch64",
        _ => architecture?.Trim().ToLowerInvariant() ?? string.Empty,
    };

    public static bool IsCompatible(string? archive, string? machine) =>
        Normalize(archive) is { Length: > 0 } value && value != "unknown" && value == Normalize(machine);
}
