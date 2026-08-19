using System;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public sealed class ProjectVersionItemViewModel(KKProjectVersion version)
{
    public KKProjectVersion Version { get; } = version;

    public string Tag => Version.Tag;

    public string CreatedAtDisplay =>
        Version.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy, HH:mm");

    public string ArtifactSizeDisplay => Version.ArtifactSize switch
    {
        >= 1_073_741_824 => $"{Version.ArtifactSize / 1_073_741_824d:F2} ГБ",
        >= 1_048_576 => $"{Version.ArtifactSize / 1_048_576d:F2} МБ",
        >= 1024 => $"{Version.ArtifactSize / 1024d:F1} КБ",
        _ => $"{Version.ArtifactSize} Б",
    };

    public string Description => string.IsNullOrWhiteSpace(Version.Description)
        ? "Без описания"
        : Version.Description;
}
