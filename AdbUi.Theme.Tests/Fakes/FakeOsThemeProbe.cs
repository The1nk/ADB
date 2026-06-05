using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>Test double for <see cref="IOsThemeProbe"/>. Set <see cref="Current"/> and call
/// <see cref="RaiseChanged"/> to simulate an OS theme change.</summary>
public sealed class FakeOsThemeProbe : IOsThemeProbe
{
    public AppTheme Current { get; set; } = AppTheme.Light;

    public event EventHandler? OsThemeChanged;

    public void RaiseChanged(AppTheme newTheme)
    {
        Current = newTheme;
        OsThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}
