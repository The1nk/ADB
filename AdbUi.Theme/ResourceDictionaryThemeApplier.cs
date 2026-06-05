using System.Windows;

namespace AdbUi.Theme;

/// <summary>Live <see cref="IThemeApplier"/>: merges the active theme's brush dictionary into the WPF
/// application resources, removing any previously-applied theme dictionary so exactly one is ever present.
/// The shared <c>Controls.xaml</c> is merged once by the app at startup and is never swapped.</summary>
public sealed class ResourceDictionaryThemeApplier : IThemeApplier
{
    private const string AssemblyName = "AdbUi.Theme";
    private ResourceDictionary? _current;

    public void Apply(AppTheme theme)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No WPF Application is running.");

        var next = new ResourceDictionary { Source = ThemeUri(theme) };

        var merged = app.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(next);
        _current = next;
    }

    private static Uri ThemeUri(AppTheme theme)
    {
        var file = theme switch
        {
            AppTheme.Dark => "Dark",
            AppTheme.HighContrast => "HighContrast",
            _ => "Light",
        };
        return new Uri($"pack://application:,,,/{AssemblyName};component/Themes/{file}.xaml", UriKind.Absolute);
    }
}
