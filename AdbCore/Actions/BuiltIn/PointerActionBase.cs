using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Input pointer actions (Click / Right Click / Double Click / Mouse Move):
/// resolves the target window HWND, reads X/Y and the input method, and dispatches to the chosen
/// <see cref="IInputSender"/>. Subclasses only name themselves and pick which sender call to make.</summary>
public abstract class PointerActionBase : IActionDefinition, IActionExecutor
{
    public const string XKey = "x";
    public const string YKey = "y";
    public const string MethodKey = "method";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly InputSenderResolver _senders;

    protected PointerActionBase(InputSenderResolver senders)
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
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = XKey, Label = "X", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField { Key = YKey, Label = "Y", Type = ConfigFieldType.Number, DefaultValue = 0 },
        new ConfigField
        {
            Key = MethodKey,
            Label = "Input Method",
            Type = ConfigFieldType.Enum,
            DefaultValue = InputSenderResolver.SendInputMethod,
            Options = new() { InputSenderResolver.SendInputMethod, InputSenderResolver.PostMessageMethod },
        },
    };
    public bool SupportsRetry => false;

    /// <summary>Dispatches the specific pointer operation (click/right-click/double-click/move) to the sender.</summary>
    protected abstract void Dispatch(IInputSender sender, IntPtr windowHandle, int x, int y);

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail($"{DisplayName} requires a resolved Window target (HWND)."));
        }

        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        var method = ConfigValues.GetString(context.Action.Config, MethodKey, InputSenderResolver.SendInputMethod);
        Dispatch(_senders.Resolve(method), hwnd, x, y);

        return Task.FromResult(ActionResult.Ok(SuccessPort));
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
