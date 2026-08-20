using System;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public sealed class ProjectVersionItemViewModel(
    KKProjectVersion version,
    ILocalizationService? localizationService = null) : ViewModelBase
{
    public KKProjectVersion Version { get; } = version;

    public string Tag => Version.Tag;

    public string CreatedAtDisplay =>
        Version.CreatedAtUtc.ToLocalTime().ToString("g");

    public string ArtifactSizeDisplay => Version.ArtifactSize switch
    {
        >= 1_073_741_824 => $"{Version.ArtifactSize / 1_073_741_824d:F2} {Localize("ГБ")}",
        >= 1_048_576 => $"{Version.ArtifactSize / 1_048_576d:F2} {Localize("МБ")}",
        >= 1024 => $"{Version.ArtifactSize / 1024d:F1} {Localize("КБ")}",
        _ => $"{Version.ArtifactSize} {Localize("Б")}",
    };

    public string Description => string.IsNullOrWhiteSpace(Version.Description)
        ? Localize("Без описания")
        : Version.Description;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(CreatedAtDisplay));
        OnPropertyChanged(nameof(ArtifactSizeDisplay));
        OnPropertyChanged(nameof(Description));
    }

    private string Localize(string key) => localizationService?.Get(key) ?? key;
}
