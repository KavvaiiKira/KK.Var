using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class MainViewModel
{
    private readonly IRemoteMachineService? _remoteMachineService;
    private readonly ISshPasswordStore? _profilePasswordStore;
    private MachineCandidate? _pendingMachineCandidate;
    private RemoteMachine? _machineEditorOriginal;
    private int _machineEditorRevision;
    private sealed record MachineCandidate(RemoteMachine Machine, RemoteMachineSettings Connection,
        DateTime? ExpectedUpdatedAt, RemoteConnectionCheckResult Result);

    public ObservableCollection<RemoteMachine> MachineProfiles { get; } = [];
    public ObservableCollection<RemoteMachine> AvailableMachines { get; } = [];

    [ObservableProperty]
    public partial RemoteMachine? SelectedHistoryMachine { get; set; }

    partial void OnSelectedHistoryMachineChanged(RemoteMachine? value) => _ = LoadHistoryAsync();

    [ObservableProperty]
    public partial RemoteMachine? SelectedMachineProfile { get; set; }

    [ObservableProperty]
    public partial string MachineProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMachineProfileLoading { get; set; }

    [ObservableProperty]
    public partial string MachineIdentityMessage { get; set; } = string.Empty;

    public string MachineProjectsDisplay => SelectedMachineProfile is { } machine ?
        string.Join(", ", machine.Projects.Select(project => project.Name)) : string.Empty;

    public string ConfirmMachineIdentityCaption => Localize(_pendingMachineCandidate?.Result.RequiresHostKeyConfirmation == true ?
        "Подтвердить fingerprint и проверить вход" : _machineEditorOriginal is null ?
            "Подтвердить и сохранить профиль" : "Да, это та же машина");

    public string ArchiveMachineCaption => Localize(SelectedMachineProfile?.IsArchived == true ?
        "Восстановить профиль" : "Архивировать профиль");

    public bool IsMachineFingerprintMatch => _pendingMachineCandidate is { } candidate &&
        _machineEditorOriginal?.HostKeyFingerprint == candidate.Result.HostKeyFingerprint;

    public bool HasMachineIdentityWarning => _pendingMachineCandidate is not null && !IsMachineFingerprintMatch;

    public async Task ReloadMachineProfilesAsync()
    {
        if (_remoteMachineService is null)
        {
            return;
        }
        var selectedId = SelectedMachineProfile?.Id;
        var historyMachineId = SelectedHistoryMachine?.Id;
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
        SelectedHistoryMachine = MachineProfiles.FirstOrDefault(machine => machine.Id == historyMachineId);
        await ProjectEditor.ReloadMachinesAsync();
        IsFirstRunSetupRequired = !IsRemoteMachineConfigured;
        OnPropertyChanged(nameof(IsRemoteMachineConfigured));
        OnPropertyChanged(nameof(MachineProjectsDisplay));
    }

    partial void OnSelectedMachineProfileChanged(RemoteMachine? value) => _ = LoadMachineEditorAsync(value);

    private async Task LoadMachineEditorAsync(RemoteMachine? machine)
    {
        var revision = ++_machineEditorRevision;
        InvalidatePendingMachineVerification();
        IsMachineProfileLoading = true;
        try
        {
            var password = machine is null || _profilePasswordStore is null ? null :
                await _profilePasswordStore.LoadAsync(machine.Id);
            if (revision != _machineEditorRevision)
            {
                return;
            }
            _machineEditorOriginal = machine;
            MachineProfileName = machine?.Name ?? string.Empty;
            Settings.RemoteMachine = new RemoteMachineSettings
            {
                Host = machine?.Host,
                Port = machine?.Port ?? 22,
                UserName = machine?.UserName,
                AuthenticationMethod = machine?.AuthenticationMethod ?? PrivateKeyAuthentication,
                PrivateKeyPath = machine?.PrivateKeyPath,
                Password = password,
                Architecture = machine?.Architecture,
                HostKeyFingerprint = machine?.HostKeyFingerprint,
                HostKeyHost = machine?.Host,
                HostKeyPort = machine?.Port,
            };
            AuthenticationMethod = Localize(Settings.RemoteMachine.AuthenticationMethod);
            RemoteMachineArchitecture = FormatArchitecture(machine?.Architecture);
            OnPropertyChanged(nameof(Settings));
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
                Settings.RemoteMachine = new RemoteMachineSettings
                {
                    Host = machine?.Host, Port = machine?.Port ?? 22, UserName = machine?.UserName,
                    AuthenticationMethod = machine?.AuthenticationMethod ?? PrivateKeyAuthentication,
                    PrivateKeyPath = machine?.PrivateKeyPath, HostKeyFingerprint = machine?.HostKeyFingerprint,
                    HostKeyHost = machine?.Host, HostKeyPort = machine?.Port, Architecture = machine?.Architecture,
                };
                AuthenticationMethod = Localize(Settings.RemoteMachine.AuthenticationMethod);
                OnPropertyChanged(nameof(Settings));
                SettingsError = exception.Message;
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
        SelectedMachineProfile = null;
        _ = LoadMachineEditorAsync(null);
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
        Host = source.Host?.Trim(), Port = source.Port, UserName = source.UserName?.Trim(),
        AuthenticationMethod = source.AuthenticationMethod, PrivateKeyPath = source.PrivateKeyPath,
        Password = source.Password, HostKeyFingerprint = source.HostKeyFingerprint,
        HostKeyHost = source.HostKeyHost, HostKeyPort = source.HostKeyPort, Architecture = source.Architecture,
    };

    private async Task VerifyMachineProfileAsync()
    {
        if (_remoteMachineService is null || _remoteConnectionService is null ||
            IsConnectionCheckRunning || IsMachineProfileLoading)
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
            SettingsError = Localize("Укажите имя профиля машины.");
            return;
        }
        IsConnectionCheckRunning = true;
        var revision = _machineEditorRevision;
        try
        {
            var original = _machineEditorOriginal;
            if (original?.IsArchived == true)
            {
                throw new InvalidOperationException(Localize("Сначала восстановите архивированный профиль."));
            }
            if (original is not null)
            {
                await _remoteMachineService.AssertEditableAsync(original.Id);
            }
            var connection = CopyConnection(Settings.RemoteMachine);
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
                Id = original?.Id ?? Guid.NewGuid(), Name = MachineProfileName,
            }, connection, original?.UpdatedAtUtc, result);
            var addressChanged = original is not null && (original.Host != connection.Host || original.Port != connection.Port);
            if (addressChanged || result.RequiresHostKeyConfirmation)
            {
                _pendingMachineCandidate = candidate;
                PendingHostKeyFingerprint = result.HostKeyFingerprint;
                MachineIdentityMessage = LocalizeFormat(
                    "Старый адрес: {0}\nНовый адрес: {1}\nСтарый fingerprint: {2}\nНовый fingerprint: {3}",
                    original is null ? "—" : $"{original.Host}:{original.Port}",
                    $"{connection.Host}:{connection.Port}", original?.HostKeyFingerprint ?? "—", result.HostKeyFingerprint);
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
            SettingsError = exception.Message;
        }
        finally
        {
            IsConnectionCheckRunning = false;
        }
    }

    private async Task ConfirmMachineIdentityAsync()
    {
        if (_pendingMachineCandidate is not { } candidate || IsConnectionCheckRunning ||
            _remoteConnectionService is null)
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
                SettingsError = result.ErrorMessage.Length == 0 ? Localize("SSH-ключ снова изменился. Повторите проверку.") : result.ErrorMessage;
                return;
            }
            if (candidate.Result.RequiresHostKeyConfirmation && candidate.ExpectedUpdatedAt.HasValue)
            {
                _pendingMachineCandidate = candidate with { Result = result };
                MachineIdentityMessage += Environment.NewLine + LocalizeFormat("Проверенная архитектура нового адреса: {0}", result.Architecture);
                OnPropertyChanged(nameof(ConfirmMachineIdentityCaption));
                return;
            }
            await SaveMachineCandidateAsync(candidate with { Result = result }, identityConfirmed: true);
        }
        catch (Exception exception)
        {
            SettingsError = exception.Message;
        }
        finally
        {
            IsConnectionCheckRunning = false;
        }
    }

    private async Task SaveMachineCandidateAsync(MachineCandidate candidate, bool identityConfirmed)
    {
        if (_remoteMachineService is null)
        {
            return;
        }
        candidate.Connection.HostKeyFingerprint = candidate.Result.HostKeyFingerprint;
        var machine = await _remoteMachineService.SaveVerifiedAsync(candidate.Machine, candidate.Connection,
            candidate.Result, candidate.ExpectedUpdatedAt, identityConfirmed);
        InvalidatePendingMachineVerification();
        InvalidateDeploymentPreflight();
        await ReloadMachineProfilesAsync();
        SelectedMachineProfile = MachineProfiles.First(item => item.Id == machine.Id);
        Settings.IsFirstRunCompleted = true;
        if (_userSettingsService is not null)
        {
            await _userSettingsService.SaveAsync(Settings);
        }
        SettingsError = string.Empty;
        SettingsStatus = Localize("Профиль машины сохранён и проверен.");
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
        Settings.RemoteMachine.Host = host;
        Settings.RemoteMachine.Port = port;
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand]
    private async Task ArchiveMachineProfileAsync()
    {
        if (_remoteMachineService is null || SelectedMachineProfile is not { } machine)
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
            InvalidateDeploymentPreflight();
        }
        catch (Exception exception)
        {
            SettingsError = exception.Message;
        }
    }
}
