using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class GitHubSourceSelectionViewModel : ViewModelBase
    {
        private readonly IGitHubService _gitHub;

        private readonly IGitHubAuthenticationService _authentication;

        private readonly ILocalizationService _localization;

        private string? _repository;

        private CancellationTokenSource? _commitCancellation;

        private CancellationTokenSource? _branchesCancellation;

        private bool _configuring;

        private int _repositoryRevision;

        public GitHubSourceSelectionViewModel(
            IGitHubService gitHub,
            IGitHubAuthenticationService authentication,
            ILocalizationService localization)
        {
            _gitHub = gitHub;
            _authentication = authentication;
            _localization = localization;
            Configure(null, null);
        }

        public ObservableCollection<GitHubBranchOption> Branches { get; } = [];

        public event EventHandler? BranchChanged;

        [ObservableProperty]
        public partial GitHubBranchOption? SelectedBranch { get; set; }

        [ObservableProperty]
        public partial GitHubCommit? Commit { get; set; }

        [ObservableProperty]
        public partial bool IsResolving { get; set; }

        [ObservableProperty]
        public partial bool IsLoadingBranches { get; set; }

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        public string BranchName => SelectedBranch?.Name ?? string.Empty;

        public string? Repository => _repository;

        public bool HasCommit => Commit is not null && !IsResolving && !IsLoadingBranches && ErrorMessage.Length == 0;

        public bool HasError => ErrorMessage.Length > 0;

        public void Configure(string? repository, string? branch)
        {
            _configuring = true;
            _repositoryRevision++;
            _repository = repository;
            _commitCancellation?.Cancel();
            _branchesCancellation?.Cancel();
            Branches.Clear();
            Branches.Add(new GitHubBranchOption(string.Empty, Texts.GitHubDefaultBranch));
            if (!string.IsNullOrWhiteSpace(branch))
            {
                Branches.Add(new GitHubBranchOption(branch, branch));
            }
            SelectedBranch = Branches.Last();
            Commit = null;
            ErrorMessage = string.Empty;
            _configuring = false;
        }

        public void RefreshLocalization()
        {
            var selected = BranchName;
            _configuring = true;
            var previous = Branches.FirstOrDefault(option => option.Name.Length == 0);
            if (previous is not null)
            {
                Branches.Remove(previous);
            }
            Branches.Insert(0, new GitHubBranchOption(string.Empty, Texts.GitHubDefaultBranch));
            SelectedBranch = Branches.FirstOrDefault(option => option.Name == selected);
            _configuring = false;
            ErrorMessage = string.Empty;
        }

        public async Task LoadBranchesAsync()
        {
            if (_repository is null || IsLoadingBranches)
            {
                return;
            }
            var repository = _repository;
            var revision = _repositoryRevision;
            using var cancellation = new CancellationTokenSource();
            _branchesCancellation = cancellation;
            IsLoadingBranches = true;
            ErrorMessage = string.Empty;
            try
            {
                var token = await GetTokenAsync(cancellation.Token);
                var branches = await _gitHub.GetBranchesAsync(repository, token.AccessToken, cancellation.Token);
                if (revision != _repositoryRevision)
                {
                    return;
                }
                var selected = BranchName;
                _configuring = true;
                Branches.Clear();
                Branches.Add(new GitHubBranchOption(string.Empty, Texts.GitHubDefaultBranch));
                foreach (var branch in branches.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    Branches.Add(new GitHubBranchOption(branch, branch));
                }
                if (!Branches.Any(option => option.Name == selected))
                {
                    Branches.Add(new GitHubBranchOption(selected, selected));
                }
                SelectedBranch = Branches.First(option => option.Name == selected);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (revision == _repositoryRevision)
                {
                    ErrorMessage = Localize(exception.Message);
                }
            }
            finally
            {
                _configuring = false;
                _branchesCancellation = null;
                IsLoadingBranches = false;
            }
        }

        public async Task RefreshCommitAsync()
        {
            _commitCancellation?.Cancel();
            if (_repository is null)
            {
                return;
            }
            var revision = _repositoryRevision;
            var repository = _repository;
            var branch = BranchName;
            using var cancellation = new CancellationTokenSource();
            _commitCancellation = cancellation;
            Commit = null;
            IsResolving = true;
            ErrorMessage = string.Empty;
            try
            {
                var token = await GetTokenAsync(cancellation.Token);
                var commit = await _gitHub.GetBranchCommitAsync(
                    repository,
                    branch,
                    token.AccessToken,
                    cancellation.Token);
                if (!cancellation.IsCancellationRequested && revision == _repositoryRevision)
                {
                    Commit = commit;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested && revision == _repositoryRevision)
                {
                    ErrorMessage = Localize(exception.Message);
                }
            }
            finally
            {
                if (ReferenceEquals(_commitCancellation, cancellation))
                {
                    _commitCancellation = null;
                    IsResolving = false;
                }
            }
        }

        private async Task<GitHubToken> GetTokenAsync(CancellationToken cancellationToken) =>
            await _authentication!.GetTokenAsync(cancellationToken) ??
            throw new InvalidOperationException(Texts.ConnectGitHubSettingsBuildingProject);

        partial void OnSelectedBranchChanged(GitHubBranchOption? value)
        {
            OnPropertyChanged(nameof(BranchName));
            if (!_configuring)
            {
                BranchChanged?.Invoke(this, EventArgs.Empty);
                _ = RefreshCommitAsync();
            }
        }

        partial void OnCommitChanged(GitHubCommit? value)
        {
            OnPropertyChanged(nameof(HasCommit));
        }

        partial void OnIsResolvingChanged(bool value) => OnPropertyChanged(nameof(HasCommit));

        partial void OnIsLoadingBranchesChanged(bool value) => OnPropertyChanged(nameof(HasCommit));

        partial void OnErrorMessageChanged(string value)
        {
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasCommit));
        }

        private string Localize(string key) => _localization.Get(key);
    }
}
