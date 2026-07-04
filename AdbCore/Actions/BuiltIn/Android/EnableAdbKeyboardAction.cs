using AdbCore.Android;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Makes the ADBKeyboard IME active (for Unicode Send Text), waits until the device reports it
/// active plus a short settle, and stashes the previously-active IME id into a run variable so a later
/// Restore Keyboard node can put it back. The wait means no manual Delay is needed before a broadcast.</summary>
public sealed class EnableAdbKeyboardAction : AndroidActionBase
{
    public const string PreviousImeVarKey = "previousImeVar";
    public const string DefaultPreviousImeVar = "PreviousIme";
    public const string SettleMsKey = "settleMs";
    public const int DefaultSettleMs = 400;

    private readonly int _maxWaitMs;
    private readonly int _pollIntervalMs;

    public EnableAdbKeyboardAction() : this(maxWaitMs: 3000, pollIntervalMs: 150) { }

    /// <summary>Test/tuning seam: smaller timings make the timeout path fast to exercise. The action
    /// registry only ever calls the parameterless constructor.</summary>
    public EnableAdbKeyboardAction(int maxWaitMs, int pollIntervalMs)
    {
        _maxWaitMs = maxWaitMs;
        _pollIntervalMs = pollIntervalMs;
    }

    public override string TypeKey => "android.enableAdbKeyboard";
    public override string DisplayName => "Enable ADB Keyboard";
    public override string Description => "Activates the ADBKeyboard IME (for Unicode text), waits for it to become active, and remembers the previous keyboard.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = PreviousImeVarKey,
            Label = "Remember Previous IME In",
            Type = ConfigFieldType.String,
            DefaultValue = DefaultPreviousImeVar,
        },
        new ConfigField
        {
            Key = SettleMsKey,
            Label = "Settle (ms)",
            Type = ConfigFieldType.Number,
            DefaultValue = DefaultSettleMs,
        },
    };

    public override async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return RequiresDevice();
        }

        if (!device.IsInputMethodAvailable(AndroidImes.AdbKeyboard))
        {
            return ActionResult.Fail(
                "ADBKeyboard is not installed on the device. Install the ADBKeyboard APK (e.g. with the Install APK action) and try again.");
        }

        // Capture and stash the prior IME BEFORE switching, so Restore can recover it even if the
        // activation below times out.
        var previous = device.GetInputMethod();
        var varName = ConfigValues.GetString(context.Action.Config, PreviousImeVarKey, DefaultPreviousImeVar);
        if (!string.IsNullOrWhiteSpace(varName))
        {
            context.Context.Variables[varName] = previous;
        }

        device.EnableInputMethod(AndroidImes.AdbKeyboard);
        device.SetInputMethod(AndroidImes.AdbKeyboard);

        // The IME switch is asynchronous ON THE DEVICE: poll until it reports ADBKeyboard active.
        var waited = 0;
        while (!string.Equals(device.GetInputMethod(), AndroidImes.AdbKeyboard, StringComparison.Ordinal))
        {
            if (waited >= _maxWaitMs)
            {
                return ActionResult.Fail(
                    $"ADBKeyboard did not become the active input method within {_maxWaitMs} ms — the device may be slow to switch keyboards; try again or add a Delay.");
            }
            await Task.Delay(_pollIntervalMs, ct);
            waited += _pollIntervalMs;
        }

        // Settle: give ADBKeyboard's broadcast receiver time to register before any broadcast is sent.
        var settleMs = Math.Max(0, ConfigValues.GetInt(context.Action.Config, SettleMsKey, DefaultSettleMs));
        if (settleMs > 0)
        {
            await Task.Delay(settleMs, ct);
        }

        return ActionResult.Ok(SuccessPort);
    }
}
