using AdbUi.Theme;

namespace AdbUi.Theme.Tests.Fakes;

/// <summary>Records every theme it was asked to apply.</summary>
public sealed class FakeThemeApplier : IThemeApplier
{
    public List<AppTheme> Applied { get; } = new();

    public AppTheme? Last => Applied.Count == 0 ? null : Applied[^1];

    public void Apply(AppTheme theme) => Applied.Add(theme);
}
