namespace AdbUi.Theme;

/// <summary>Applies an effective <see cref="AppTheme"/> to the running UI (swaps the active brush dictionary).
/// Abstracted so <see cref="ThemeManager"/>'s logic is testable without a live WPF Application.</summary>
public interface IThemeApplier
{
    void Apply(AppTheme theme);
}
