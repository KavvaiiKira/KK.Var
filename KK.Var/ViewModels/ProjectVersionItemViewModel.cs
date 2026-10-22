using System;
using KK.Var.Constants;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public sealed class ProjectVersionItemViewModel(
        KKProjectVersion version,
        ILocalizationService localizationService,
        bool artifactExists = true,
        bool isProtected = false,
        long? artifactSize = null) : ViewModelBase
    {
        public KKProjectVersion Version { get; } = version;

        public string TargetArchitecture => string.IsNullOrWhiteSpace(Version.TargetArchitecture) || Version.TargetArchitecture == ArchitectureNames.Unknown ?
            Texts.UnknownArchitecture :
            Version.TargetArchitecture;

        public string Tag => Version.Tag;

        public string CreatedAtDisplay => Version.CreatedAtUtc.ToLocalTime().ToString("g");

        public string ArtifactSizeDisplay
        {
            get
            {
                if (!ArtifactExists)
                {
                    return "-";
                }

                var size = artifactSize ?? Version.ArtifactSize;
                return size switch
                {
                    >= 1_073_741_824 => $"{size / 1_073_741_824d:F2} {Texts.GB}",
                    >= 1_048_576 => $"{size / 1_048_576d:F2} {Texts.MB}",
                    >= 1024 => $"{size / 1024d:F1} {Texts.KB}",
                    _ => $"{size} {Texts.B}",
                };
            }
        }

        public bool ArtifactExists { get; } = artifactExists;

        public bool IsArtifactMissing => !ArtifactExists;

        public bool IsProtected { get; } = isProtected;

        public bool CanDelete => !IsProtected;

        public bool IsPinned => Version.IsPinned;

        public string PinActionText =>
            Version.IsPinned ? Texts.Unpin : Texts.Pin;

        public string AvailabilityDisplay =>
            ArtifactExists ? Texts.ArchiveAvailable : Texts.ArchiveMissing;

        public string PinnedDisplay => Texts.Pinned;

        public string ProtectedDisplay => Texts.ProtectedDeletion;

        public bool HasSourceCommit => !string.IsNullOrWhiteSpace(Version.SourceCommitSha);

        public string SourceCommitDisplay =>
            Version.SourceCommitSha is { Length: > 0 } commit ?
                $"Git {commit[..Math.Min(8, commit.Length)]}" :
                string.Empty;

        public string Description =>
            string.IsNullOrWhiteSpace(Version.Description) ?
                Texts.NoDescription :
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

        private string Localize(string key) => localizationService.Get(key);
    }
}
