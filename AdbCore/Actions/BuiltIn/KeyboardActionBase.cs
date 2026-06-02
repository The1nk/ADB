using AdbCore.Execution;
using AdbCore.Input;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for keyboard Input actions (Type Text / Key Press). Adds the per-node
/// "Key Delay (ms)" field that paces synthetic key events so fast targets don't drop or auto-repeat
/// keys, and exposes the resolved delay to subclasses.</summary>
public abstract class KeyboardActionBase : InputActionBase
{
    public const string KeyDelayKey = "keyDelayMs";

    /// <summary>Default inter-key delay (ms) applied after each synthetic key down/up event. Reliable
    /// for normal desktop apps out of the box; user-overridable per node.</summary>
    public const int DefaultKeyDelayMs = 20;

    protected KeyboardActionBase(InputSenderResolver senders) : base(senders)
    {
    }

    /// <summary>The keyboard action's own config fields, shown before the shared Key Delay + Input Method fields.</summary>
    protected abstract IEnumerable<ConfigField> KeyboardConfigFields { get; }

    protected override IEnumerable<ConfigField> ActionConfigFields =>
    [
        .. KeyboardConfigFields,
        new ConfigField
        {
            Key = KeyDelayKey,
            Label = "Key Delay (ms)",
            Type = ConfigFieldType.Number,
            DefaultValue = DefaultKeyDelayMs,
        },
    ];

    /// <summary>Resolves the configured inter-key delay (ms), defaulting to <see cref="DefaultKeyDelayMs"/>.</summary>
    protected int KeyDelayMs(ActionExecutionContext context)
        => ConfigValues.GetInt(context.Action.Config, KeyDelayKey, DefaultKeyDelayMs);
}
