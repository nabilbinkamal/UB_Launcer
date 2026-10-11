using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Launcher.Core.ViewModels;

namespace Launcher.UI.Controls;

public interface INavigationAwarePage
{
    void SetNavigationActive(bool isActive);
}

public sealed class NavigationHost : Panel
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<NavigationHost, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<ViewModelBase?> CurrentViewProperty =
        AvaloniaProperty.Register<NavigationHost, ViewModelBase?>(nameof(CurrentView));

    private readonly Dictionary<ViewModelBase, Control> pages = [];
    private readonly ViewLocator viewLocator = new();
    private INotifyCollectionChanged? collectionNotifier;
    private Window? observedWindow;
    private MainWindowViewModel? observedViewModel;

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ViewModelBase? CurrentView
    {
        get => GetValue(CurrentViewProperty);
        set => SetValue(CurrentViewProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            SubscribeToItemsSource();
            EnsurePages();
        }
        else if (change.Property == CurrentViewProperty)
        {
            if (CurrentView is not null)
            {
                EnsurePage(CurrentView);
            }

            UpdateVisibility();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateVisibility();
        }
        else if (change.Property == DataContextProperty)
        {
            ObserveViewModel();
            UpdateVisibility();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        observedWindow = TopLevel.GetTopLevel(this) as Window;
        if (observedWindow is not null)
            observedWindow.PropertyChanged += OnWindowPropertyChanged;
        ObserveViewModel();
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (observedWindow is not null)
            observedWindow.PropertyChanged -= OnWindowPropertyChanged;
        if (observedViewModel is not null)
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        observedWindow = null;
        observedViewModel = null;
        foreach (var page in pages.Values.OfType<INavigationAwarePage>())
            page.SetNavigationActive(false);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveViewModel()
    {
        if (observedViewModel is not null)
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        observedViewModel = DataContext as MainWindowViewModel;
        if (observedViewModel is not null)
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsAnyModalOpen))
            UpdateVisibility();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty || e.Property == Window.WindowStateProperty)
            UpdateVisibility();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            child.Measure(availableSize);

            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var bounds = new Rect(finalSize);
        foreach (var child in Children)
        {
            if (child.IsVisible)
                child.Arrange(bounds);
        }

        return finalSize;
    }

    private void SubscribeToItemsSource()
    {
        if (collectionNotifier is not null)
        {
            collectionNotifier.CollectionChanged -= OnItemsChanged;
        }

        collectionNotifier = ItemsSource as INotifyCollectionChanged;
        if (collectionNotifier is not null)
        {
            collectionNotifier.CollectionChanged += OnItemsChanged;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
    {
        EnsurePages();
        UpdateVisibility();
    }

    private void EnsurePages()
    {
        if (ItemsSource is null)
        {
            return;
        }

        foreach (var item in ItemsSource.OfType<ViewModelBase>())
        {
            EnsurePage(item);
        }
    }

    private void EnsurePage(ViewModelBase viewModel)
    {
        if (pages.ContainsKey(viewModel))
        {
            return;
        }

        var page = viewLocator.Build(viewModel) ?? new TextBlock { Text = "Page unavailable" };
        page.DataContext = viewModel;
        pages.Add(viewModel, page);
        Children.Add(page);
        UpdatePageState(viewModel, page);
    }

    private void UpdateVisibility()
    {
        foreach (var (viewModel, page) in pages)
        {
            UpdatePageState(viewModel, page);
        }
    }

    private void UpdatePageState(ViewModelBase viewModel, Control page)
    {
        var isActive = IsVisible && ReferenceEquals(viewModel, CurrentView);
        page.IsVisible = isActive;
        page.Opacity = 1;
        page.IsHitTestVisible = isActive;

        if (page is INavigationAwarePage navigationAwarePage)
        {
            navigationAwarePage.SetNavigationActive(isActive &&
                observedWindow is { IsActive: true, WindowState: not WindowState.Minimized } &&
                observedViewModel?.IsAnyModalOpen != true);
        }
    }
}
