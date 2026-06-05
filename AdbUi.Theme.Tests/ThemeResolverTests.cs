using AdbUi.Theme;

namespace AdbUi.Theme.Tests;

public class ThemeResolverTests
{
    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.HighContrast)]
    public void System_resolves_to_the_os_theme(AppTheme osTheme)
    {
        Assert.Equal(osTheme, ThemeResolver.Resolve(ThemeSelection.System, osTheme));
    }

    [Theory]
    [InlineData(ThemeSelection.Light, AppTheme.Light)]
    [InlineData(ThemeSelection.Dark, AppTheme.Dark)]
    [InlineData(ThemeSelection.HighContrast, AppTheme.HighContrast)]
    public void Explicit_selection_ignores_the_os_theme(ThemeSelection selection, AppTheme expected)
    {
        // OS theme is deliberately different from the selection to prove it is ignored.
        Assert.Equal(expected, ThemeResolver.Resolve(selection, AppTheme.Dark));
        Assert.Equal(expected, ThemeResolver.Resolve(selection, AppTheme.Light));
    }
}
