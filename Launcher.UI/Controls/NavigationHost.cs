using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredWidth = 0d;
        var desiredHeight = 0d;
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            if (!child.IsVisible)
            {
                continue;
            }

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
        var isActive = ReferenceEquals(viewModel, CurrentView);
        page.IsVisible = isActive;
        page.Opacity = 1;
        page.IsHitTestVisible = isActive;

        if (page is INavigationAwarePage navigationAwarePage)
        {
            navigationAwarePage.SetNavigationActive(isActive);
        }
    }
}
