using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public ObservableCollection<ViewModelBase> Views { get; } = [];

    public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        var viewModel = GetOrCreate(typeof(TViewModel));
        if (ReferenceEquals(CurrentView, viewModel))
        {
            return;
        }

        CurrentView = viewModel;
    }

    public void Preload<TViewModel>() where TViewModel : ViewModelBase =>
        GetOrCreate(typeof(TViewModel));

    private ViewModelBase GetOrCreate(Type type)
    {
        if (_cache.TryGetValue(type, out var viewModel))
        {
            return viewModel;
        }

        viewModel = _factory(type);
        _cache[type] = viewModel;
        Views.Add(viewModel);
        return viewModel;
    }
}
