using System.Collections.ObjectModel;
using Launcher.Core.ViewModels;

namespace Launcher.Core.Services;

public interface INavigationService
{
    ViewModelBase CurrentView { get; set; }
    ObservableCollection<ViewModelBase> Views { get; }
    string? ErrorMessage { get; }
    bool NavigateTo<TViewModel>() where TViewModel : ViewModelBase;
    void Preload<TViewModel>() where TViewModel : ViewModelBase;
}
