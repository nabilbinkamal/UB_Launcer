using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Launcher.Core.ViewModels;

public partial class PerformanceViewModel : ViewModelBase
{
    [ObservableProperty] private int _allocatedRamGb = 8;
    [ObservableProperty] private bool _enableOptimizationPack = true;
    [ObservableProperty] private bool _enableFastLaunch = true;
    [ObservableProperty] private bool _enableGameMode = true;
    [ObservableProperty] private string _statusText = "Performance profile is ready.";

    [RelayCommand]
    private void ApplyPerformance()
    {
        StatusText = $"Performance settings applied with {AllocatedRamGb} GB RAM.";
    }
}
