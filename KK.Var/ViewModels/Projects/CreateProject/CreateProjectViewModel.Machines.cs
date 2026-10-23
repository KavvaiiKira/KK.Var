using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Models;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        public ObservableCollection<RemoteMachine> ProjectMachines { get; } = [];

        [ObservableProperty]
        public partial RemoteMachine? SelectedProjectMachine { get; set; }

        [ObservableProperty]
        public partial bool MachineSwitchConfirmed { get; set; }

        public bool IsProjectMachineChanged => _editingProject is not null &&
            _editingProject.RemoteMachineId != SelectedProjectMachine?.Id;

        public async Task ReloadMachinesAsync()
        {
            var machines = await _remoteMachineService.GetAllAsync();
            SetMachines(machines);
        }

        public void SetMachines(IEnumerable<RemoteMachine> machines)
        {
            var selectedId = SelectedProjectMachine?.Id ?? _editingProject?.RemoteMachineId;
            ProjectMachines.Clear();
            foreach (var machine in machines)
            {
                if (machine.IsVerified && !machine.IsArchived)
                {
                    ProjectMachines.Add(machine);
                }
            }
            var resetting = _isResetting;
            _isResetting = true;
            SelectedProjectMachine = ProjectMachines.FirstOrDefault(machine => machine.Id == selectedId) ??
                (_editingProject is null ? ProjectMachines.FirstOrDefault() : null);
            _isResetting = resetting;
        }

        partial void OnSelectedProjectMachineChanged(RemoteMachine? value)
        {
            MachineSwitchConfirmed = false;
            OnPropertyChanged(nameof(IsProjectMachineChanged));
            MarkDirty();
        }

        partial void OnMachineSwitchConfirmedChanged(bool value) => MarkDirty();
    }
}
