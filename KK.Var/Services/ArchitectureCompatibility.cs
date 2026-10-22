using KK.Var.Constants;

namespace KK.Var.Services
{
    public static class ArchitectureCompatibility
    {
        public static string Normalize(string? architecture) => architecture?.Trim().ToLowerInvariant() switch
        {
            ArchitectureNames.Amd64 or ArchitectureNames.X64 or ArchitectureNames.X8664 => ArchitectureNames.X8664,
            ArchitectureNames.Arm64 or ArchitectureNames.Aarch64 => ArchitectureNames.Aarch64,
            _ => architecture?.Trim().ToLowerInvariant() ?? string.Empty,
        };

        public static bool IsCompatible(string? archive, string? machine) =>
            Normalize(archive) is { Length: > 0 } value && value != ArchitectureNames.Unknown && value == Normalize(machine);
    }
}
