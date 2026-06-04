using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace BotRunner;

/// <summary>At run start, launches a Playwright browser per Browser target and stores the bound
/// <see cref="IBrowserPage"/> as the handle. A bad engine / missing browsers is a CLI usage error (exit 2).</summary>
public static class BrowserTargetBinder
{
    public static async Task BindAsync(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        foreach (var target in targets.Values.Where(t => t.Type == BotTargetType.Browser))
        {
            var engine = BrowserSelector.ParseEngine(target.Selector)
                ?? throw new CommandLineException($"Browser target selector '{target.Selector}' must be 'browser:<engine>'.");

            if (!BrowserSelector.Engines.Contains(engine))
            {
                throw new CommandLineException(
                    $"Unknown browser engine '{engine}'. Use chromium, firefox, or webkit.");
            }

            try
            {
                target.Handle = await PlaywrightBrowserPage.LaunchAsync(engine, headless: false);
            }
            catch (Exception ex)
            {
                throw new CommandLineException(
                    $"Could not launch the '{engine}' browser (have you run `playwright install`? see README): {ex.Message}");
            }
        }
    }
}
