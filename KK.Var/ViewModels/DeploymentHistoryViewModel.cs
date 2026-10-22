using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class DeploymentHistoryViewModel(
        IDeploymentHistoryService _history,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private const string UsDateFormat = "MM/dd/yyyy";

        private const string DayFirstDateFormat = "dd/MM/yyyy";

        private const string IsoDateFormat = "yyyy-MM-dd";

        private const string DottedDateFormat = "dd.MM.yyyy";

        internal const int PageSize = 20;

        private CancellationTokenSource? _historySearchCancellation;

        private int _historyRevision;

        private int _appliedHistoryRevision = -1;

        private int _activeLoads;

        private bool _isUpdatingFilters;

        public ObservableCollection<DeploymentHistoryItemViewModel> HistoryItems { get; } = [];

        public ObservableCollection<ProjectFilterOption> HistoryProjectFilters { get; } = [];

        public ObservableCollection<EnumOption> HistoryStatusFilters { get; } = [];

        [ObservableProperty]
        public partial string HistorySearchText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial ProjectFilterOption? SelectedHistoryProject { get; set; }

        [ObservableProperty]
        public partial DeploymentStatus? SelectedHistoryStatus { get; set; }

        [ObservableProperty]
        public partial bool HasMoreHistoryItems { get; private set; }

        public EnumOption? SelectedHistoryStatusFilter
        {
            get => HistoryStatusFilters.FirstOrDefault(option => Equals(option.Value, SelectedHistoryStatus));
            set
            {
                if (!_isUpdatingFilters)
                {
                    SelectedHistoryStatus = value?.Value is DeploymentStatus status ? status : null;
                }
            }
        }

        public bool HasNoHistoryItems => HistoryItems.Count == 0;

        public async Task LoadHistoryAsync(IEnumerable<KKProject> projects)
        {
            _historySearchCancellation?.Cancel();
            _historyRevision++;
            _isUpdatingFilters = true;

            try
            {
                var selectedProjectId = SelectedHistoryProject?.Id;
                HistoryProjectFilters.Clear();
                HistoryProjectFilters.Add(new ProjectFilterOption(null, null, Texts.AllProjects));

                foreach (var project in projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
                {
                    HistoryProjectFilters.Add(new ProjectFilterOption(project.Id, project.Name, project.Name));
                }

                SelectedHistoryProject = HistoryProjectFilters.FirstOrDefault(item => item.Id == selectedProjectId) ??
                    HistoryProjectFilters[0];
                RefreshHistoryStatusFilters();

                _isUpdatingFilters = false;
                await LoadHistoryPageAsync(reset: true, CancellationToken.None);
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
            }
            finally
            {
                _isUpdatingFilters = false;
            }
        }

        public async Task LoadMoreHistoryAsync()
        {
            try
            {
                await LoadHistoryPageAsync(reset: false, CancellationToken.None);
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
            }
        }

        private async Task LoadHistoryPageAsync(bool reset, CancellationToken cancellationToken)
        {
            if (!reset && (IsLoading || _appliedHistoryRevision != _historyRevision))
            {
                return;
            }

            var revision = _historyRevision;
            _activeLoads++;
            IsLoading = true;

            try
            {
                var search = HistorySearchText.Trim();

                DateTime? startedFromUtc = null;
                DateTime? startedBeforeUtc = null;

                if (TryParseHistoryDate(search, out var localDate))
                {
                    startedFromUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified));
                    startedBeforeUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified));
                    search = string.Empty;
                }

                var deployments = await _history.SearchAsync(
                    SelectedHistoryProject?.ProjectName,
                    search,
                    startedFromUtc,
                    startedBeforeUtc,
                    SelectedHistoryStatus,
                    reset ? 0 : HistoryItems.Count,
                    PageSize + 1,
                    cancellationToken,
                    RemoteMachineId);

                cancellationToken.ThrowIfCancellationRequested();
                if (revision != _historyRevision)
                {
                    return;
                }

                if (reset)
                {
                    HistoryItems.Clear();
                }

                _appliedHistoryRevision = revision;
                HasMoreHistoryItems = deployments.Count > PageSize;

                foreach (var deployment in deployments.Take(PageSize))
                {
                    HistoryItems.Add(new DeploymentHistoryItemViewModel(
                        deployment,
                        _localizationService));
                }

                OnPropertyChanged(nameof(HasNoHistoryItems));
            }
            finally
            {
                _activeLoads--;
                IsLoading = _activeLoads > 0;
            }
        }

        private async Task ReloadHistoryAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(300, cancellationToken);
                await LoadHistoryPageAsync(reset: true, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
            }
        }

        private bool TryParseHistoryDate(string value, out DateTime date)
        {
            var formats = _localizationService.CurrentLanguage == ApplicationLanguage.English ?
                new[] { UsDateFormat, DayFirstDateFormat, IsoDateFormat, DottedDateFormat } :
                new[] { DayFirstDateFormat, DottedDateFormat, IsoDateFormat, UsDateFormat };

            return DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        partial void OnHistorySearchTextChanged(string value) => ScheduleReload();

        partial void OnSelectedHistoryProjectChanged(ProjectFilterOption? value) => ScheduleReload();

        partial void OnSelectedHistoryStatusChanged(DeploymentStatus? value)
        {
            OnPropertyChanged(nameof(SelectedHistoryStatusFilter));
            ScheduleReload();
        }

        private void ScheduleReload()
        {
            if (_isUpdatingFilters)
            {
                return;
            }

            _historyRevision++;
            _historySearchCancellation?.Cancel();
            _historySearchCancellation?.Dispose();
            _historySearchCancellation = new CancellationTokenSource();
            _ = ReloadHistoryAfterDelayAsync(_historySearchCancellation.Token);
        }

        public void RefreshHistoryStatusFilters()
        {
            var selectedStatus = SelectedHistoryStatus;
            var wasUpdatingFilters = _isUpdatingFilters;
            _isUpdatingFilters = true;
            try
            {
                HistoryStatusFilters.Clear();
                HistoryStatusFilters.Add(new EnumOption(null, Texts.AllStatuses));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Succeeded, Texts.Succeeded));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Failed, Texts.Failed));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Interrupted, Texts.Interrupted));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Cancelled, Texts.Cancelled));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Running, Texts.Running));
                HistoryStatusFilters.Add(new EnumOption(DeploymentStatus.Pending, Texts.Pending));
                SelectedHistoryStatus = selectedStatus;
                OnPropertyChanged(nameof(SelectedHistoryStatusFilter));
            }
            finally
            {
                _isUpdatingFilters = wasUpdatingFilters;
            }
        }

        public override void RefreshLocalization()
        {
            _isUpdatingFilters = true;
            try
            {
                if (HistoryProjectFilters.Count > 0)
                {
                    var allProjects = HistoryProjectFilters[0];
                    HistoryProjectFilters[0] = allProjects with { Label = Texts.AllProjects };
                    if (SelectedHistoryProject?.Id is null)
                    {
                        SelectedHistoryProject = HistoryProjectFilters[0];
                    }
                }
                RefreshHistoryStatusFilters();
            }
            finally
            {
                _isUpdatingFilters = false;
            }
            base.RefreshLocalization();
        }

        [ObservableProperty]
        public partial bool IsLoading { get; private set; }

        public Guid? RemoteMachineId { get; set; }
    }
}
