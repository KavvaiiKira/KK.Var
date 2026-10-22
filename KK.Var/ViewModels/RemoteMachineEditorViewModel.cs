using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class RemoteMachineEditorViewModel(
        IRemoteMachineService remoteMachineService,
        ISshPasswordStore profilePasswordStore,
        IRemoteConnectionService _remoteConnectionService,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        [ObservableProperty]
        public partial RemoteMachineSettings MachineConnection { get; set; } = new RemoteMachineSettings();

        private readonly IRemoteMachineService _remoteMachineService = remoteMachineService;

        private readonly ISshPasswordStore _profilePasswordStore = profilePasswordStore;

        private MachineCandidate? _pendingMachineCandidate;

        private RemoteMachine? _machineEditorOriginal;

        private int _machineEditorRevision;

        private bool _isRefreshingAuthenticationMethods;

        public ObservableCollection<RemoteMachine> MachineProfiles { get; } = [];

        public ObservableCollection<RemoteMachine> AvailableMachines { get; } = [];

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNewMachineProfile))]
        public partial RemoteMachine? SelectedMachineProfile { get; set; }

        [ObservableProperty]
        public partial string MachineProfileName { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsMachineProfileLoading { get; private set; }

        [ObservableProperty]
        public partial string MachineIdentityMessage { get; private set; } = string.Empty;

        public bool IsNewMachineProfile => SelectedMachineProfile is null;

        public string MachineProjectsDisplay => SelectedMachineProfile is { } machine ?
            string.Join(", ", machine.Projects.Select(project => project.Name)) : string.Empty;

        public string ConfirmMachineIdentityCaption => Localize(_pendingMachineCandidate?.Result.RequiresHostKeyConfirmation == true ?
            Texts.ConfirmFingerprintVerifyLogin : _machineEditorOriginal is null ?
                Texts.ConfirmSaveProfile :
                Texts.YesSameMachine);

        public string ArchiveMachineCaption => Localize(SelectedMachineProfile?.IsArchived == true ?
            Texts.RestoreProfile :
            Texts.ArchiveProfile);

        public bool IsMachineFingerprintMatch => _pendingMachineCandidate is { } candidate &&
            _machineEditorOriginal?.HostKeyFingerprint == candidate.Result.HostKeyFingerprint;

        public bool HasMachineIdentityWarning => _pendingMachineCandidate is not null && !IsMachineFingerprintMatch;

        public async Task ReloadMachineProfilesAsync()
        {
            var selectedId = SelectedMachineProfile?.Id;
            var machines = await _remoteMachineService.GetAllAsync();
            MachineProfiles.Clear();
            AvailableMachines.Clear();
            foreach (var machine in machines)
            {
                MachineProfiles.Add(machine);
                if (machine.IsVerified && !machine.IsArchived)
                {
                    AvailableMachines.Add(machine);
                }
            }
            SelectedMachineProfile = MachineProfiles.FirstOrDefault(machine => machine.Id == selectedId) ??
                MachineProfiles.FirstOrDefault(machine => !machine.IsArchived);
            if (ProfilesReloaded is { } reloaded)
            {
                await reloaded();
            }
            OnPropertyChanged(nameof(IsRemoteMachineConfigured));
            OnPropertyChanged(nameof(MachineProjectsDisplay));
        }

        partial void OnSelectedMachineProfileChanged(RemoteMachine? value) => _ = LoadMachineEditorAsync(value);

        private async Task LoadMachineEditorAsync(RemoteMachine? machine)
        {
            var revision = ++_machineEditorRevision;
            InvalidatePendingMachineVerification();
            SettingsError = string.Empty;
            SettingsStatus = string.Empty;
            IsMachineProfileLoading = true;
            try
            {
                var password = machine is null ?
                    null :
                    await _profilePasswordStore.LoadAsync(machine.Id);
                if (revision != _machineEditorRevision)
                {
                    return;
                }
                _machineEditorOriginal = machine;
                MachineProfileName = machine?.Name ?? string.Empty;
                MachineConnection = new RemoteMachineSettings
                {
                    Host = machine?.Host,
                    Port = machine?.Port ?? 22,
                    UserName = machine?.UserName,
                    AuthenticationMethod = machine?.AuthenticationMethod ?? SshAuthenticationMethod.PrivateKey,
                    PrivateKeyPath = machine?.PrivateKeyPath,
                    Password = password,
                    Architecture = machine?.Architecture,
                    HostKeyFingerprint = machine?.HostKeyFingerprint,
                    HostKeyHost = machine?.Host,
                    HostKeyPort = machine?.Port,
                };
                AuthenticationMethod = MachineConnection.AuthenticationMethod;
                RemoteMachineArchitecture = FormatArchitecture(machine?.Architecture);
                OnPropertyChanged(nameof(MachineConnection));
                OnPropertyChanged(nameof(MachineProjectsDisplay));
                OnPropertyChanged(nameof(ConfirmMachineIdentityCaption));
                OnPropertyChanged(nameof(ArchiveMachineCaption));
            }
            catch (Exception exception)
            {
                if (revision == _machineEditorRevision)
                {
                    _machineEditorOriginal = machine;
                    MachineProfileName = machine?.Name ?? string.Empty;
                    MachineConnection = new RemoteMachineSettings
                    {
                        Host = machine?.Host,
                        Port = machine?.Port ?? 22,
                        UserName = machine?.UserName,
                        AuthenticationMethod = machine?.AuthenticationMethod ?? SshAuthenticationMethod.PrivateKey,
                        PrivateKeyPath = machine?.PrivateKeyPath,
                        HostKeyFingerprint = machine?.HostKeyFingerprint,
                        HostKeyHost = machine?.Host,
                        HostKeyPort = machine?.Port,
                        Architecture = machine?.Architecture,
                    };
                    AuthenticationMethod = MachineConnection.AuthenticationMethod;
                    OnPropertyChanged(nameof(MachineConnection));
                    SettingsError = Localize(exception.Message);
                }
            }
            finally
            {
                if (revision == _machineEditorRevision)
                {
                    IsMachineProfileLoading = false;
                }
            }
        }

        [RelayCommand]
        private void AddMachineProfile()
        {
            if (IsConnectionCheckRunning)
            {
                return;
            }
            if (SelectedMachineProfile is null)
            {
                _ = LoadMachineEditorAsync(null);
            }
            else
            {
                SelectedMachineProfile = null;
            }
        }

        public void InvalidatePendingMachineVerification()
        {
            _pendingMachineCandidate = null;
            PendingHostKeyFingerprint = string.Empty;
            IsHostKeyConfirmationRequired = false;
            MachineIdentityMessage = string.Empty;
            OnPropertyChanged(nameof(IsMachineFingerprintMatch));
            OnPropertyChanged(nameof(HasMachineIdentityWarning));
        }

        private static RemoteMachineSettings CopyConnection(RemoteMachineSettings source) => new RemoteMachineSettings
        {
            Host = source.Host?.Trim(),
            Port = source.Port,
            UserName = source.UserName?.Trim(),
            AuthenticationMethod = source.AuthenticationMethod,
            PrivateKeyPath = source.PrivateKeyPath,
            Password = source.Password,
            HostKeyFingerprint = source.HostKeyFingerprint,
            HostKeyHost = source.HostKeyHost,
            HostKeyPort = source.HostKeyPort,
            Architecture = source.Architecture,
        };

        public async Task VerifyMachineProfileAsync()
        {
            if (IsConnectionCheckRunning || IsMachineProfileLoading)
            {
                return;
            }
            SettingsError = ValidateRemoteMachineSettings(requireTrustedHostKey: false);
            if (!string.IsNullOrWhiteSpace(SettingsError))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(MachineProfileName))
            {
                SettingsError = Texts.EnterMachineProfileName;
                return;
            }
            IsConnectionCheckRunning = true;
            var revision = _machineEditorRevision;
            try
            {
                var original = _machineEditorOriginal;
                if (original?.IsArchived == true)
                {
                    throw new InvalidOperationException(Texts.RestoreArchivedProfileFirst);
                }
                if (original is not null)
                {
                    await _remoteMachineService.AssertEditableAsync(original.Id);
                }
                var connection = CopyConnection(MachineConnection);
                var result = await _remoteConnectionService.CheckAsync(connection);
                if (revision != _machineEditorRevision)
                {
                    return;
                }
                if (!result.IsSuccessful && !result.RequiresHostKeyConfirmation)
                {
                    SettingsError = result.ErrorMessage;
                    return;
                }
                var candidate = new MachineCandidate(new RemoteMachine
                {
                    Id = original?.Id ?? Guid.NewGuid(),
                    Name = MachineProfileName,
                }, connection, original?.UpdatedAtUtc, result);
                var addressChanged = original is not null && (original.Host != connection.Host || original.Port != connection.Port);
                if (addressChanged || result.RequiresHostKeyConfirmation)
                {
                    _pendingMachineCandidate = candidate;
                    PendingHostKeyFingerprint = result.HostKeyFingerprint;
                    MachineIdentityMessage = LocalizeFormat(
                        Texts.OldAddressNewAddressOldFingerprintNew,
                        original is null ? "-" : $"{original.Host}:{original.Port}",
                        $"{connection.Host}:{connection.Port}",
                        original?.HostKeyFingerprint ?? "-",
                        result.HostKeyFingerprint);
                    IsHostKeyConfirmationRequired = true;
                    OnPropertyChanged(nameof(ConfirmMachineIdentityCaption));
                    OnPropertyChanged(nameof(IsMachineFingerprintMatch));
                    OnPropertyChanged(nameof(HasMachineIdentityWarning));
                    return;
                }
                await SaveMachineCandidateAsync(candidate, identityConfirmed: false);
            }
            catch (Exception exception)
            {
                SettingsError = Localize(exception.Message);
            }
            finally
            {
                IsConnectionCheckRunning = false;
            }
        }

        public async Task ConfirmMachineIdentityAsync()
        {
            if (_pendingMachineCandidate is not { } candidate || IsConnectionCheckRunning)
            {
                return;
            }
            IsConnectionCheckRunning = true;
            try
            {
                candidate.Connection.HostKeyFingerprint = candidate.Result.HostKeyFingerprint;
                var result = await _remoteConnectionService.CheckAsync(candidate.Connection);
                if (!result.IsSuccessful)
                {
                    SettingsError = result.ErrorMessage.Length == 0 ? Texts.SSHKeyChangedAgainRepeatCheck : result.ErrorMessage;
                    return;
                }
                if (candidate.Result.RequiresHostKeyConfirmation && candidate.ExpectedUpdatedAt.HasValue)
                {
                    _pendingMachineCandidate = candidate with { Result = result };
                    MachineIdentityMessage += Environment.NewLine + LocalizeFormat(
                        Texts.VerifiedArchitectureNewAddress,
                        result.Architecture);
                    OnPropertyChanged(nameof(ConfirmMachineIdentityCaption));
                    return;
                }
                await SaveMachineCandidateAsync(candidate with { Result = result }, identityConfirmed: true);
            }
            catch (Exception exception)
            {
                SettingsError = Localize(exception.Message);
            }
            finally
            {
                IsConnectionCheckRunning = false;
            }
        }

        private async Task SaveMachineCandidateAsync(MachineCandidate candidate, bool identityConfirmed)
        {
            candidate.Connection.HostKeyFingerprint = candidate.Result.HostKeyFingerprint;
            var machine = await _remoteMachineService.SaveVerifiedAsync(
                candidate.Machine,
                candidate.Connection,
                candidate.Result,
                candidate.ExpectedUpdatedAt,
                identityConfirmed);
            InvalidatePendingMachineVerification();
            PreflightInvalidated?.Invoke();
            await ReloadMachineProfilesAsync();
            SelectedMachineProfile = MachineProfiles.First(item => item.Id == machine.Id);
            if (ProfileVerified is { } verified)
            {
                await verified();
            }
            SettingsError = string.Empty;
            SettingsStatus = Texts.MachineProfileSavedVerified;
        }

        [RelayCommand]
        private void CreateNewMachineFromCandidate()
        {
            if (_pendingMachineCandidate is not { } candidate)
            {
                return;
            }
            var host = candidate.Connection.Host;
            var port = candidate.Connection.Port;
            AddMachineProfile();
            MachineConnection.Host = host;
            MachineConnection.Port = port;
            OnPropertyChanged(nameof(MachineConnection));
        }

        [RelayCommand]
        private async Task ArchiveMachineProfileAsync()
        {
            if (SelectedMachineProfile is not { } machine)
            {
                return;
            }
            if (IsConnectionCheckRunning)
            {
                return;
            }
            try
            {
                if (machine.IsArchived)
                {
                    await _remoteMachineService.RestoreAsync(machine.Id);
                }
                else
                {
                    await _remoteMachineService.ArchiveAsync(machine.Id);
                }
                await ReloadMachineProfilesAsync();
                PreflightInvalidated?.Invoke();
            }
            catch (Exception exception)
            {
                SettingsError = Localize(exception.Message);
            }
        }

        private static string PrivateKeyAuthentication => Texts.SSHKey;

        private static string PasswordAuthentication => Texts.Password;

        [ObservableProperty]
        public partial string SettingsStatus { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SettingsError { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsConnectionCheckRunning { get; private set; }

        [ObservableProperty]
        public partial bool IsHostKeyConfirmationRequired { get; private set; }

        [ObservableProperty]
        public partial string PendingHostKeyFingerprint { get; private set; } = string.Empty;

        [ObservableProperty]
        public partial string RemoteMachineArchitecture { get; set; } =
            Texts.DetectedAutomaticallyDuringConnectionCheck;

        public ObservableCollection<EnumOption> AuthenticationMethods { get; } = [];

        [ObservableProperty]
        public partial SshAuthenticationMethod AuthenticationMethod { get; set; } = SshAuthenticationMethod.PrivateKey;

        public bool IsPrivateKeyAuthentication =>
            AuthenticationMethod == SshAuthenticationMethod.PrivateKey;

        public bool IsPasswordAuthentication =>
            AuthenticationMethod == SshAuthenticationMethod.Password;

        public bool IsRemoteMachineConfigured =>
            MachineProfiles.Any(machine => machine.IsVerified && !machine.IsArchived);

        partial void OnAuthenticationMethodChanged(SshAuthenticationMethod value)
        {
            if (_isRefreshingAuthenticationMethods)
            {
                return;
            }

            if (!IsMachineProfileLoading && !IsConnectionCheckRunning)
            {
                InvalidatePendingMachineVerification();
            }
            PreflightInvalidated?.Invoke();
            MachineConnection.AuthenticationMethod = value;
            OnPropertyChanged(nameof(IsPrivateKeyAuthentication));
            OnPropertyChanged(nameof(IsPasswordAuthentication));
        }

        public void RefreshAuthenticationMethods()
        {
            var selectedMethod = AuthenticationMethod;
            _isRefreshingAuthenticationMethods = true;
            try
            {
                AuthenticationMethods.Clear();
                AuthenticationMethods.Add(new EnumOption(SshAuthenticationMethod.PrivateKey, Localize(PrivateKeyAuthentication)));
                AuthenticationMethods.Add(new EnumOption(SshAuthenticationMethod.Password, Localize(PasswordAuthentication)));
                AuthenticationMethod = selectedMethod;
            }
            finally
            {
                _isRefreshingAuthenticationMethods = false;
            }
        }

        private string ValidateRemoteMachineSettings(bool requireTrustedHostKey = true)
        {
            if (string.IsNullOrWhiteSpace(MachineConnection.Host))
            {
                return Texts.EnterRemoteMachineAddress;
            }

            if (MachineConnection.Port is < 1 or > 65535)
            {
                return Texts.SSHPortMustBetween165535;
            }

            if (string.IsNullOrWhiteSpace(MachineConnection.UserName))
            {
                return Texts.EnterSSHUser;
            }

            if (IsPrivateKeyAuthentication &&
                string.IsNullOrWhiteSpace(MachineConnection.PrivateKeyPath))
            {
                return Texts.EnterPrivateSSHKeyPath;
            }

            if (IsPasswordAuthentication &&
                string.IsNullOrWhiteSpace(MachineConnection.Password))
            {
                return Texts.EnterSSHPassword;
            }

            if (requireTrustedHostKey &&
                (string.IsNullOrWhiteSpace(MachineConnection.HostKeyFingerprint) ||
                 !string.Equals(
                     MachineConnection.HostKeyHost,
                     MachineConnection.Host?.Trim(),
                     StringComparison.OrdinalIgnoreCase) ||
                 MachineConnection.HostKeyPort != MachineConnection.Port))
            {
                return Texts.CheckConnectionConfirmSSHServerFingerprint;
            }

            return string.Empty;
        }

        public string FormatArchitecture(string? architecture) =>
            string.IsNullOrWhiteSpace(architecture) ?
                Texts.DetectedAutomaticallyDuringConnectionCheck :
                LocalizeFormat(Texts.DetectedAutomatically, architecture);

        public event Action? PreflightInvalidated;

        public event Func<Task>? ProfilesReloaded;

        public event Func<Task>? ProfileVerified;
    }
}
