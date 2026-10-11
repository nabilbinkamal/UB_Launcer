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
    private string? errorMessage;

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

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

    public bool NavigateTo<TViewModel>() where TViewModel : ViewModelBase
    {
        try
        {
            var viewModel = GetOrCreate(typeof(TViewModel));
            ErrorMessage = null;
            CurrentView = viewModel;
            return true;
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "Could not open launcher page {Page}", typeof(TViewModel).Name);
            ErrorMessage = exception is System.IO.FileLoadException
                ? "Windows could not load a required component. Ask your administrator to approve the launcher release, then try again."
                : "This page could not be opened. Your current page is still available. Try again or restart the launcher.";
            return false;
        }
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
