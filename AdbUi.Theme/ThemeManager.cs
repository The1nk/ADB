namespace AdbUi.Theme;

/// <summary>Owns the app's current theme selection. Resolves it (consulting the OS when "System"), applies the
/// effective theme via <see cref="IThemeApplier"/>, persists the choice, and re-applies on OS theme changes
/// while the selection follows the OS.</summary>
public sealed class ThemeManager
{
    private readonly ISettingsStore _store;
    private readonly IOsThemeProbe _osProbe;
    private readonly IThemeApplier _applier;

    public ThemeManager(ISettingsStore store, IOsThemeProbe osProbe, IThemeApplier applier)
    {
        _store = store;
        _osProbe = osProbe;
        _applier = applier;
        _osProbe.OsThemeChanged += OnOsThemeChanged;
    }

    /// <summary>The user's current theme choice.</summary>
    public ThemeSelection CurrentSelection { get; private set; } = ThemeSelection.System;

    /// <summary>Raised after <see cref="CurrentSelection"/> changes (so menus can update their checkmarks).</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Loads the persisted selection and applies the resulting theme. Call once at app startup.</summary>
    public void Initialize()
    {
        CurrentSelection = _store.Load().Theme;
        ApplyEffective();
    }

    /// <summary>Switches to <paramref name="selection"/>, applies it, and persists the choice.</summary>
    public void Apply(ThemeSelection selection)
    {
        CurrentSelection = selection;
        ApplyEffective();
        _store.Save(new AppSettings { Theme = selection });
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyEffective() =>
        _applier.Apply(ThemeResolver.Resolve(CurrentSelection, _osProbe.Current));

    private void OnOsThemeChanged(object? sender, EventArgs e)
    {
        // Only honor OS changes while we are following the OS.
        if (CurrentSelection == ThemeSelection.System) ApplyEffective();
    }
}
