using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Launcher.Core.ViewModels;

namespace Launcher.UI;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    private readonly ConditionalWeakTable<ViewModelBase, Control> viewCache = new();

    public Control? Build(object? param)
    {
        if (param is not ViewModelBase viewModel)
            return null;

        if (viewCache.TryGetValue(viewModel, out var cachedView))
        {
            return cachedView;
        }
        
        var name = viewModel.GetType().FullName!
            .Replace("Launcher.Core.ViewModels", "Launcher.UI.Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType($"{name}, Launcher.UI");

        if (type != null)
        {
            var view = (Control)Activator.CreateInstance(type)!;
            viewCache.Add(viewModel, view);
            return view;
        }
        
        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
