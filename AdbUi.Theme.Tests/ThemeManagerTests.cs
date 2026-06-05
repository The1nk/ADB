using AdbUi.Theme;
using AdbUi.Theme.Tests.Fakes;

namespace AdbUi.Theme.Tests;

public class ThemeManagerTests
{
    private static (ThemeManager mgr, FakeThemeApplier applier, FakeOsThemeProbe probe, FakeSettingsStore store)
        Build(ThemeSelection initial, AppTheme osTheme = AppTheme.Light)
    {
        var applier = new FakeThemeApplier();
        var probe = new FakeOsThemeProbe { Current = osTheme };
        var store = new FakeSettingsStore(new AppSettings { Theme = initial });
        var mgr = new ThemeManager(store, probe, applier);
        return (mgr, applier, probe, store);
    }

    [Fact]
    public void Initialize_applies_the_saved_explicit_theme()
    {
        var (mgr, applier, _, _) = Build(ThemeSelection.Dark);

        mgr.Initialize();

        Assert.Equal(AppTheme.Dark, applier.Last);
        Assert.Equal(ThemeSelection.Dark, mgr.CurrentSelection);
    }

    [Fact]
    public void Initialize_with_System_applies_the_os_theme()
    {
        var (mgr, applier, _, _) = Build(ThemeSelection.System, osTheme: AppTheme.Dark);

        mgr.Initialize();

        Assert.Equal(AppTheme.Dark, applier.Last);
    }

    [Fact]
    public void Apply_changes_theme_and_persists_the_selection()
    {
        var (mgr, applier, _, store) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();

        mgr.Apply(ThemeSelection.HighContrast);

        Assert.Equal(AppTheme.HighContrast, applier.Last);
        Assert.Equal(ThemeSelection.HighContrast, mgr.CurrentSelection);
        Assert.Equal(ThemeSelection.HighContrast, store.Load().Theme);
    }

    [Fact]
    public void Os_change_reapplies_while_following_System()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();

        probe.RaiseChanged(AppTheme.Dark);

        Assert.Equal(AppTheme.Dark, applier.Last);
    }

    [Fact]
    public void Os_change_is_ignored_when_an_explicit_theme_is_selected()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.Light, osTheme: AppTheme.Light);
        mgr.Initialize();
        var appliedCountAfterInit = applier.Applied.Count;

        probe.RaiseChanged(AppTheme.Dark);

        Assert.Equal(appliedCountAfterInit, applier.Applied.Count); // no extra apply
        Assert.Equal(AppTheme.Light, applier.Last);
    }

    [Fact]
    public void Switching_from_System_to_explicit_then_os_change_does_nothing()
    {
        var (mgr, applier, probe, _) = Build(ThemeSelection.System, osTheme: AppTheme.Light);
        mgr.Initialize();
        mgr.Apply(ThemeSelection.Dark); // leave System

        var count = applier.Applied.Count;
        probe.RaiseChanged(AppTheme.Light);

        Assert.Equal(count, applier.Applied.Count);
    }

    [Fact]
    public void SelectionChanged_fires_on_apply()
    {
        var (mgr, _, _, _) = Build(ThemeSelection.System);
        mgr.Initialize();
        ThemeSelection? observed = null;
        mgr.SelectionChanged += (_, _) => observed = mgr.CurrentSelection;

        mgr.Apply(ThemeSelection.Dark);

        Assert.Equal(ThemeSelection.Dark, observed);
    }

    [Fact]
    public void Apply_still_applies_and_signals_when_persistence_fails()
    {
        var applier = new FakeThemeApplier();
        var probe = new FakeOsThemeProbe { Current = AppTheme.Light };
        var store = new FakeSettingsStore(new AppSettings { Theme = ThemeSelection.System }) { ThrowOnSave = true };
        var mgr = new ThemeManager(store, probe, applier);
        mgr.Initialize();
        var fired = false;
        mgr.SelectionChanged += (_, _) => fired = true;

        mgr.Apply(ThemeSelection.Dark); // must not throw despite the failing store

        Assert.Equal(AppTheme.Dark, applier.Last);
        Assert.Equal(ThemeSelection.Dark, mgr.CurrentSelection);
        Assert.True(fired);
    }
}
