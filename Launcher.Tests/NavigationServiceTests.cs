using Launcher.Core.Services;
using Launcher.Core.ViewModels;
using Xunit;

namespace Launcher.Tests;

public class NavigationServiceTests
{
    private sealed class Page : ViewModelBase;
    private sealed class OtherPage : ViewModelBase;

    [Fact]
    public void NavigationReusesPages()
    {
        var created = 0;
        var navigation = new NavigationService(_ => { created++; return new Page(); });
        Assert.True(navigation.NavigateTo<Page>());
        var first = navigation.CurrentView;
        Assert.True(navigation.NavigateTo<Page>());
        Assert.Same(first, navigation.CurrentView);
        Assert.Equal(1, created);
        Assert.Single(navigation.Views);
    }

    [Fact]
    public void BlockedComponentPreservesCurrentPageAndCanRetry()
    {
        var blocked = true;
        var navigation = new NavigationService(type => type == typeof(Page) ? new Page()
            : blocked ? throw new FileLoadException("Blocked assembly") : new OtherPage());
        Assert.True(navigation.NavigateTo<Page>());
        var original = navigation.CurrentView;
        Assert.False(navigation.NavigateTo<OtherPage>());
        Assert.Same(original, navigation.CurrentView);
        Assert.Single(navigation.Views);
        Assert.Contains("required component", navigation.ErrorMessage);
        blocked = false;
        Assert.True(navigation.NavigateTo<OtherPage>());
        Assert.Null(navigation.ErrorMessage);
        Assert.IsType<OtherPage>(navigation.CurrentView);
    }

    [Fact]
    public void ConstructorFailureIsRecoverable()
    {
        var navigation = new NavigationService(_ => throw new InvalidOperationException("fixture"));
        Assert.False(navigation.NavigateTo<Page>());
        Assert.NotNull(navigation.ErrorMessage);
        Assert.Empty(navigation.Views);
    }
}
