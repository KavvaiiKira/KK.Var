using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Data;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class ArtifactStorageViewModel(
        IArtifactStorageService _artifactStorageService,
        IArtifactStorageRelocationService _storageRelocation,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        [ObservableProperty]
        public partial string ArtifactStorageEffectivePath { get; private set; } = string.Empty;

        [ObservableProperty]
        public partial long ArtifactStorageUsedBytes { get; private set; }

        [ObservableProperty]
        public partial ArtifactStorageMigrationPlan? PendingArtifactStorageMigration { get; private set; }

        [ObservableProperty]
        public partial bool IsArtifactStorageBusy { get; private set; }

        [ObservableProperty]
        public partial string ArtifactStorageWarning { get; private set; } = string.Empty;

        public string ArtifactStorageUsedDisplay => ByteSizeFormatter.Format(
            ArtifactStorageUsedBytes,
            _localizationService);

        public bool HasPendingArtifactStorageMigration => PendingArtifactStorageMigration is not null;

        public string ArtifactStorageMigrationFileCountDisplay =>
            PendingArtifactStorageMigration is { } plan ?
                LocalizeFormat(Texts.Files, plan.FileCount) :
                string.Empty;

        public string ArtifactStorageMigrationSizeDisplay =>
            PendingArtifactStorageMigration is { } plan ?
                ByteSizeFormatter.Format(plan.TotalBytes, _localizationService) :
                string.Empty;

        public async Task PrepareArtifactStorageMigrationAsync(string? targetPath)
        {
            if (IsArtifactStorageBusy)
            {
                return;
            }

            IsArtifactStorageBusy = true;
            ErrorReported?.Invoke(string.Empty);
            ArtifactStorageWarning = string.Empty;

            try
            {
                PendingArtifactStorageMigration = await _storageRelocation.PlanMigrationAsync(
                    targetPath);
            }
            catch (Exception exception)
            {
                PendingArtifactStorageMigration = null;
                ErrorReported?.Invoke(Localize(exception.Message));
            }
            finally
            {
                IsArtifactStorageBusy = false;
            }
        }

        public async Task ApplyArtifactStorageMigrationAsync()
        {
            if (PendingArtifactStorageMigration is not { } plan ||
                IsArtifactStorageBusy)
            {
                return;
            }

            IsArtifactStorageBusy = true;
            ErrorReported?.Invoke(string.Empty);
            ArtifactStorageWarning = string.Empty;

            try
            {
                var result = await _storageRelocation.MigrateAsync(plan.TargetRoot);
                RootChanged?.Invoke(string.Equals(
                    result.Plan.TargetRoot,
                    Path.GetFullPath(DatabasePaths.ArtifactsDirectory),
                    StringComparison.OrdinalIgnoreCase) ?
                        null :
                        result.Plan.TargetRoot);
                PendingArtifactStorageMigration = null;
                ArtifactStorageWarning = result.Warning ?? string.Empty;
                StatusReported?.Invoke(Texts.LocalVersionsDirectoryChanged);
                await RefreshArtifactStorageSummaryAsync();
            }
            catch (Exception exception)
            {
                ErrorReported?.Invoke(Localize(exception.Message));
            }
            finally
            {
                IsArtifactStorageBusy = false;
            }
        }

        public void CancelArtifactStorageMigration()
        {
            PendingArtifactStorageMigration = null;
            ArtifactStorageWarning = string.Empty;
        }

        public async Task RefreshArtifactStorageSummaryAsync()
        {
            try
            {
                var summary = await _artifactStorageService.GetSummaryAsync();
                ArtifactStorageEffectivePath = summary.EffectiveRoot;
                ArtifactStorageUsedBytes = summary.UsedBytes;
            }
            catch (Exception exception)
            {
                ArtifactStorageEffectivePath = Texts.DirectoryUnavailable;
                ArtifactStorageUsedBytes = 0;
                ErrorReported?.Invoke(Localize(exception.Message));
            }

            OnPropertyChanged(nameof(ArtifactStorageUsedDisplay));
        }

        partial void OnPendingArtifactStorageMigrationChanged(
            ArtifactStorageMigrationPlan? value)
        {
            OnPropertyChanged(nameof(HasPendingArtifactStorageMigration));
            OnPropertyChanged(nameof(ArtifactStorageMigrationFileCountDisplay));
            OnPropertyChanged(nameof(ArtifactStorageMigrationSizeDisplay));
        }

        public event Action<string>? ErrorReported;

        public event Action<string>? StatusReported;

        public event Action<string?>? RootChanged;
    }
}
