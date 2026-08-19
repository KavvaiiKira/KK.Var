using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IUserSettingsService? _userSettingsService;

    // Used only by the XAML previewer.
    public MainViewModel()
    {
    }

    public MainViewModel(IUserSettingsService userSettingsService)
    {
        _userSettingsService = userSettingsService;
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
