using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels;

public partial class MainViewModel
{
    private bool _loadingNotificationPreference;
    public event Action<DeploymentCompletionNotice>? DeploymentCompleted;

    [ObservableProperty]
    public partial bool DeploymentNotificationsEnabled { get; set; } = true;

    public async Task<bool> OpenNotificationTargetAsync(DeploymentCompletionNotice notice)
    {
        if (_projectService is null || _projectDeploymentService is null)
        {
            return false;
        }
        try
        {
            var project = await _projectService.GetByIdAsync(notice.ProjectId) ??
                throw new InvalidOperationException(Localize("Проект не найден."));
            SelectedProject = project;
            await LoadProjectDetailsAsync(project);
            if (notice.DeploymentId is { } deploymentId)
            {
                var deployment = await _projectDeploymentService.GetByIdAsync(deploymentId) ??
                    throw new InvalidOperationException(Localize("Операция больше недоступна."));
                if (deployment.KKProjectId != project.Id)
                {
                    throw new InvalidOperationException(Localize("Операция больше недоступна."));
                }
                deployment.Project = project;
                await LoadDeploymentDetailsAsync(new DeploymentHistoryItemViewModel(deployment, _localizationService));
            }
            return true;
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
            return false;
        }
    }

    public string NotificationTitle(DeploymentCompletionNotice notice)
    {
        if (notice.AutomaticRollbackFailed)
        {
            return Localize("Ошибка Deploy и автоматического rollback");
        }
        if (notice.AutomaticRollbackCompleted)
        {
            return Localize("Ошибка Deploy: автоматический rollback выполнен");
        }
        return (notice.OperationType, notice.Status) switch
        {
            (DeploymentOperationType.Rollback, DeploymentStatus.Succeeded) => Localize("Rollback завершён"),
            (DeploymentOperationType.Rollback, _) => Localize("Rollback завершился ошибкой"),
            (_, DeploymentStatus.Succeeded) => Localize("Deploy завершён"),
            _ => Localize("Deploy завершился ошибкой"),
        };
    }

    partial void OnDeploymentNotificationsEnabledChanged(bool value)
    {
        Settings.DeploymentNotificationsEnabled = value;
        if (!_loadingNotificationPreference)
        {
            _ = SaveNotificationPreferenceAsync(value);
        }
    }

    private void LoadNotificationPreference()
    {
        _loadingNotificationPreference = true;
        DeploymentNotificationsEnabled = Settings.DeploymentNotificationsEnabled;
        _loadingNotificationPreference = false;
    }

    private async Task SaveNotificationPreferenceAsync(bool enabled)
    {
        if (_userSettingsService is null)
        {
            return;
        }
        try
        {
            await _userSettingsService.SaveDeploymentNotificationsEnabledAsync(enabled);
        }
        catch (Exception exception)
        {
            PublishNotification(exception.Message, isError: true);
        }
    }
}
