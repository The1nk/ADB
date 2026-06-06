using System.IO;

namespace BotBuilder.Core.Palette;

/// <summary>Availability of the tooling a palette category needs, plus a human reason when it is missing.</summary>
public sealed record DependencyStatus(bool IsAvailable, string? Reason)
{
    public static readonly DependencyStatus Available = new(true, null);
}

/// <summary>Reports whether a palette category's external dependency is present on this machine.</summary>
public interface IDependencyProbe
{
    DependencyStatus ForCategory(string category);
}

/// <summary>Live dependency probe. Android needs <c>adb</c> on the PATH; Browser needs at least one Playwright
/// browser engine in the Playwright cache. The environment checks are injectable so the category→status
/// mapping is unit-testable without touching the real PATH/filesystem.</summary>
public sealed class DependencyProbe : IDependencyProbe
{
    private readonly Func<bool> _androidAvailable;
    private readonly Func<bool> _browserAvailable;

    public DependencyProbe(Func<bool>? androidAvailable = null, Func<bool>? browserAvailable = null)
    {
        _androidAvailable = androidAvailable ?? DefaultAndroidCheck;
        _browserAvailable = browserAvailable ?? DefaultBrowserCheck;
    }

    public DependencyStatus ForCategory(string category) => category switch
    {
        "Android" => _androidAvailable()
            ? DependencyStatus.Available
            : new DependencyStatus(false, "adb not found on PATH"),
        "Browser" => _browserAvailable()
            ? DependencyStatus.Available
            : new DependencyStatus(false, "No browser engine found — run 'playwright install'"),
        _ => DependencyStatus.Available,
    };

    // adb resolvable on the PATH (how the runtime locates the ADB server).
    private static bool DefaultAndroidCheck()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(dir.Trim(), "adb.exe")))
                {
                    return true;
                }
            }
            catch
            {
                // Malformed PATH entry — skip it.
            }
        }

        return false;
    }

    // At least one Playwright browser engine present in the Playwright browsers cache.
    private static bool DefaultBrowserCheck()
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ms-playwright");
            }

            if (!Directory.Exists(root))
            {
                return false;
            }

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("chromium", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("firefox", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("webkit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Defensive: any IO failure means we can't confirm browsers → treat as unavailable.
        }

        return false;
    }
}
