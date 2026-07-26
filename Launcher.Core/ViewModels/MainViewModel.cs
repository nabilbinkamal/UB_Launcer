using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.Core.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to UBLauncher — Unleash the ultimate performance!";
}
