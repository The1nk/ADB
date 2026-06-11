using AdbCore.Android;
using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Targets;
using AdvancedSharpAdbClient;

namespace BotRunner;

/// <summary>Binds a single bot target to a live handle on demand — used by nested bots to resolve their own
/// (non-shared) targets when their card executes. Mirrors the three run-start binders (Window/Android/Browser).
/// Throws <see cref="InvalidOperationException"/> on an unresolvable selector (the nested executor turns that
/// into an onFailure result).</summary>
public sealed class RunnerTargetBinder : ITargetBinder
{
    private readonly IWindowResolver _windowResolver;
    private AdbClient? _adbClient;

    public RunnerTargetBinder(IWindowResolver windowResolver)
    {
        ArgumentNullException.ThrowIfNull(windowResolver);
        _windowResolver = windowResolver;
    }

    public async Task<ResolvedTarget> BindAsync(BotTarget target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        var resolved = new ResolvedTarget { Type = target.Type, Selector = target.Selector };

        switch (target.Type)
        {
            case BotTargetType.Window:
                resolved.Handle = BindWindow(target.Selector);
                break;
            case BotTargetType.AndroidDevice:
                resolved.Handle = BindAndroid(target.Selector);
                break;
            case BotTargetType.Browser:
                resolved.Handle = await BindBrowserAsync(target.Selector);
                break;
        }

        return resolved;
    }

    private IntPtr BindWindow(string selector)
    {
        IntPtr handle;
        try
        {
            handle = _windowResolver.Resolve(selector);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"Invalid Window target selector '{selector}': {ex.Message}");
        }

        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Could not resolve Window target selector '{selector}' to a window.");
        }

        return handle;
    }

    private IAndroidDevice BindAndroid(string selector)
    {
        var serial = AdbSelector.ParseSerial(selector)
            ?? throw new InvalidOperationException($"Android target selector '{selector}' must be 'serial:<device>'.");

        if (_adbClient is null)
        {
            try
            {
                AdbServer.Instance.StartServer(adbPath: "adb", restartServerIfNewer: false);
                _adbClient = new AdbClient();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not reach the ADB server for nested target '{selector}': {ex.Message}");
            }
        }

        var device = _adbClient.GetDevices().FirstOrDefault(d => d.Serial == serial)
            ?? throw new InvalidOperationException($"No connected Android device with serial '{serial}'.");

        return new AdvancedSharpAdbDevice(_adbClient, device);
    }

    private static async Task<IBrowserPage> BindBrowserAsync(string selector)
    {
        var engine = BrowserSelector.ParseEngine(selector)
            ?? throw new InvalidOperationException($"Browser target selector '{selector}' must be 'browser:<engine>'.");

        if (!BrowserSelector.Engines.Contains(engine))
        {
            throw new InvalidOperationException($"Unknown browser engine '{engine}'. Use chromium, firefox, or webkit.");
        }

        try
        {
            return await PlaywrightBrowserPage.LaunchAsync(engine, headless: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not launch the '{engine}' browser: {ex.Message}");
        }
    }
}
