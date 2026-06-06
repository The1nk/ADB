using System.Windows;

namespace AdbUi.Theme;

/// <summary>Live <see cref="IThemeApplier"/>: merges the active theme's brush dictionary into the WPF
/// application resources, removing any theme dictionary already present so exactly one is ever active. It
/// removes by source path, so a dark theme merged statically in App.xaml (the anti-flash baseline) is swapped
/// cleanly too. The shared <c>Controls.xaml</c> is not a theme file, so it is never removed.</summary>
public sealed class ResourceDictionaryThemeApplier : IThemeApplier
{
    private const string AssemblyName = "AdbUi.Theme";
    private static readonly string[] ThemeFiles = { "/Light.xaml", "/Dark.xaml", "/HighContrast.xaml" };

    public void Apply(AppTheme theme)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No WPF Application is running.");

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            if (IsThemeDictionary(merged[i].Source))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Add(new ResourceDictionary { Source = ThemeUri(theme) });
    }

    private static bool IsThemeDictionary(Uri? source)
    {
        var src = source?.OriginalString;
        if (src is null)
        {
            return false;
        }

        foreach (var file in ThemeFiles)
        {
            if (src.EndsWith(file, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
