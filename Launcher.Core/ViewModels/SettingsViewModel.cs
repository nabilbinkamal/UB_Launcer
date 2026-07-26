using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Launcher.Core.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _allocatedRamGb = 4; // Default to 4GB RAM

    [ObservableProperty]
    private int _minRamGb = 2;

    [ObservableProperty]
    private int _maxRamGb = 16;

    [ObservableProperty]
    private string _javaRuntimePath = "Detecting system JVM...";

    [ObservableProperty]
    private string _customJvmArguments = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch";

    [ObservableProperty]
    private bool _enableDiscordRpc = true;

    [ObservableProperty]
    private bool _enableOverlay = true;

    public SettingsViewModel()
    {
        // Default constructor
    }

    [RelayCommand]
    private void ResetToDefault()
    {
        AllocatedRamGb = 4;
        JavaRuntimePath = "Detecting system JVM...";
        CustomJvmArguments = "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+AlwaysPreTouch";
        EnableDiscordRpc = true;
        EnableOverlay = true;
        Log.Information("Launcher settings reverted to default values.");
    }

    [RelayCommand]
    private void SaveSettings()
    {
        // Persistent save will be wired to SQLite/LiteDB in subsequent phases
        Log.Information("Launcher settings successfully saved: RAM={Ram}GB, DiscordRPC={Rpc}, Overlay={Overlay}", 
            AllocatedRamGb, EnableDiscordRpc, EnableOverlay);
    }
}
