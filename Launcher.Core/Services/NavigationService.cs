using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Core.ViewModels;

namespace Launcher.Core.Services;

public class NavigationService : ObservableObject, INavigationService
{
    private ViewModelBase _currentView = null!;
    private readonly Dictionary<Type, ViewModelBase> _cache = new();
    private readonly Func<Type, ViewModelBase> _factory;

    public NavigationService(Func<Type, ViewModelBase> factory)
    {
        _factory = factory;
        // Default navigate is handled by the bootstrapper
    }

    public ViewModelBase CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        var type = typeof(TViewModel);
        if (!_cache.TryGetValue(type, out var viewModel))
        {
            viewModel = _factory(type);
            _cache[type] = viewModel;
        }
        CurrentView = viewModel;
    }
}
