using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Models;

namespace KK.Var.ViewModels
{
    public partial class MainViewModel
    {
        [ObservableProperty]
        public partial RemoteMachine? SelectedHistoryMachine { get; set; }

        partial void OnSelectedHistoryMachineChanged(RemoteMachine? value) => _ = LoadHistoryAsync();

        public Task LoadHistoryAsync()
        {
            History.RemoteMachineId = SelectedHistoryMachine?.Id;
            return History.LoadHistoryAsync(ProjectList.Projects);
        }
    }
}
