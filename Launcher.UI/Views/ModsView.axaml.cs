using Avalonia.Controls;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Views;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is ModsViewModel vm)
            {
                _ = vm.EnsureLoadedAsync();
            }
        };
    }
}
