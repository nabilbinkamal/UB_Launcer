using Launcher.Core.ViewModels;

namespace Launcher.Core.Services;

public interface INavigationService
{
    ViewModelBase CurrentView { get; set; }
    void NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
}
