using CommunityToolkit.Mvvm.ComponentModel;

namespace KK.Var.ViewModels;

public partial class EnvironmentVariableRowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}
