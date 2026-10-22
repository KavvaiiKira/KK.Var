using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Configuration;
using KK.Var.Enums;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        public async Task LoadSettingsAsync()
        {
            await AppSettings.LoadAsync();
            await ProjectList.LoadAsync();
        }

        [RelayCommand]
        private async Task SaveSettingsAsync() => await MachineEditor.VerifyMachineProfileAsync();

        [RelayCommand]
        private async Task CheckRemoteConnectionAsync() => await MachineEditor.VerifyMachineProfileAsync();

        [RelayCommand]
        private async Task ConfirmHostKeyAsync() => await MachineEditor.ConfirmMachineIdentityAsync();

        [RelayCommand]
        private void CancelHostKeyConfirmation() => MachineEditor.InvalidatePendingMachineVerification();

        public void ClearSettingsStatus()
        {
            MachineEditor.SettingsStatus = string.Empty;
            MachineEditor.SettingsError = string.Empty;

            ClearStatusNotification();
        }

        private void HandleSettingsStatusChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (HasSuccessNotification)
                {
                    ClearStatusNotification();
                }

                return;
            }

            PublishNotification(value, isError: false);
        }

        private void HandleSettingsErrorChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (HasErrorNotification)
                {
                    ClearStatusNotification();
                }

                return;
            }

            PublishNotification(value, isError: true);
        }
    }
}
