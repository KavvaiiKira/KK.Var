using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        public event Action<DeploymentCompletionNotice>? DeploymentCompleted;

        public async Task<bool> OpenNotificationTargetAsync(DeploymentCompletionNotice notice)
        {
            try
            {
                var project = await ProjectList.GetByIdAsync(notice.ProjectId) ??
                    throw new InvalidOperationException(Localize("Проект не найден."));
                SelectedProject = project;
                await LoadProjectDetailsAsync(project);
                if (notice.DeploymentId is { } deploymentId)
                {
                    var deployment = await _deploymentHistory.GetByIdAsync(deploymentId) ??
                        throw new InvalidOperationException(Localize("Операция больше недоступна."));
                    if (deployment.KKProjectId != project.Id)
                    {
                        throw new InvalidOperationException(Localize("Операция больше недоступна."));
                    }
                    deployment.Project = project;
                    await LoadDeploymentDetailsAsync(new DeploymentHistoryItemViewModel(
                        deployment,
                        _localizationService));
                }
                return true;
            }
            catch (Exception exception)
            {
                PublishNotification(Localize(exception.Message), isError: true);
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

        [ObservableProperty]
        public partial string NotificationMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool IsNotificationError { get; set; }

        public bool HasNotification => !string.IsNullOrWhiteSpace(NotificationMessage);

        public bool HasErrorNotification => HasNotification && IsNotificationError;

        public bool HasSuccessNotification => HasNotification && !IsNotificationError;

        public void ClearStatusNotification()
        {
            _versionTagError = null;
            _failedVersionTag = null;
            NotificationMessage = string.Empty;
            IsNotificationError = false;
        }

        public void ShowNotification(string message, bool isError) =>
            PublishNotification(message, isError);

        partial void OnNotificationMessageChanged(string value)
        {
            OnPropertyChanged(nameof(HasNotification));
            OnPropertyChanged(nameof(HasErrorNotification));
            OnPropertyChanged(nameof(HasSuccessNotification));
        }

        partial void OnIsNotificationErrorChanged(bool value)
        {
            OnPropertyChanged(nameof(HasErrorNotification));
            OnPropertyChanged(nameof(HasSuccessNotification));
        }

        private void PublishNotification(string message, bool isError)
        {
            _versionTagError = null;
            _failedVersionTag = null;
            IsNotificationError = isError;
            NotificationMessage = message;
        }
    }
}
