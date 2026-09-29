using System;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public sealed class ProjectVersionItemViewModel(
    KKProjectVersion version,
    bool artifactExists = true,
    bool isProtected = false,
    long? artifactSize = null,
    ILocalizationService? localizationService = null) : ViewModelBase
{
    public KKProjectVersion Version { get; } = version;

    public string Tag => Version.Tag;

    public string CreatedAtDisplay => Version.CreatedAtUtc.ToLocalTime().ToString("g");

    public string ArtifactSizeDisplay
    {
        get
        {
            if (!ArtifactExists)
            {
                return "—";
            }

            var size = artifactSize ?? Version.ArtifactSize;
            return size switch
            {
                >= 1_073_741_824 => $"{size / 1_073_741_824d:F2} {Localize("ГБ")}",
                >= 1_048_576 => $"{size / 1_048_576d:F2} {Localize("МБ")}",
                >= 1024 => $"{size / 1024d:F1} {Localize("КБ")}",
                _ => $"{size} {Localize("Б")}",
            };
        }
    }

    public bool ArtifactExists { get; } = artifactExists;

    public bool IsArtifactMissing => !ArtifactExists;

    public bool IsProtected { get; } = isProtected;

    public bool CanDelete => !IsProtected;

    public bool IsPinned => Version.IsPinned;

    public string PinActionText =>
        Version.IsPinned ? Localize("Открепить") : Localize("Закрепить");

    public string AvailabilityDisplay =>
        ArtifactExists ? Localize("Архив доступен") : Localize("Архив отсутствует");

    public string PinnedDisplay => Localize("Закреплена");

    public string ProtectedDisplay => Localize("Защищена от удаления");

    public bool HasSourceCommit => !string.IsNullOrWhiteSpace(Version.SourceCommitSha);

    public string SourceCommitDisplay =>
        Version.SourceCommitSha is { Length: > 0 } commit ?
            $"Git {commit[..Math.Min(8, commit.Length)]}" :
            string.Empty;

    public string Description =>
        string.IsNullOrWhiteSpace(Version.Description) ?
            Localize("Без описания") :
            Version.Description;

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(CreatedAtDisplay));
        OnPropertyChanged(nameof(ArtifactSizeDisplay));
        OnPropertyChanged(nameof(HasSourceCommit));
        OnPropertyChanged(nameof(SourceCommitDisplay));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(PinActionText));
        OnPropertyChanged(nameof(AvailabilityDisplay));
        OnPropertyChanged(nameof(PinnedDisplay));
        OnPropertyChanged(nameof(ProtectedDisplay));
    }

    private string Localize(string key) => localizationService?.Get(key) ?? key;
}
