using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input actions: resolves the target window HWND, exposes the input-method
/// config field + ports, and runs the chosen <see cref="IInputSender"/>. Subclasses contribute their own
/// config fields (via <see cref="ActionConfigFields"/>) and the actual operation (via <see cref="Perform"/>).</summary>
public abstract class InputActionBase : IActionDefinition, IActionExecutor
{
    public const string MethodKey = "method";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly InputSenderResolver _senders;
    private List<ConfigField>? _configFields;

    protected InputActionBase(InputSenderResolver senders)
    {
        ArgumentNullException.ThrowIfNull(senders);
        _senders = senders;
    }

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Input";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    /// <summary>The subclass's own config fields, shown before the shared Input Method field.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        new ConfigField
        {
            Key = MethodKey,
            Label = "Input Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = InputSenderResolver.SendInputMethod,
            Options = new() { InputSenderResolver.SendInputMethod, InputSenderResolver.PostMessageMethod },
        },
    ];

    public bool SupportsRetry => false;

    /// <summary>Runs the action's operation against the resolved window and chosen sender; returns the result.</summary>
    protected abstract Task<ActionResult> PerformAsync(IInputSender sender, IntPtr windowHandle, ActionExecutionContext context, CancellationToken ct);

    public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND).");
        }

        var method = ConfigValues.GetString(context.Action.Config, MethodKey, InputSenderResolver.SendInputMethod);
        return await PerformAsync(_senders.Resolve(method), hwnd, context, ct);
    }

    /// <summary>Resolves the action's target HWND: the explicit TargetId, or the sole target if unset.</summary>
    private static IntPtr? ResolveWindow(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;

        return target?.Handle as IntPtr?;
    }
}
