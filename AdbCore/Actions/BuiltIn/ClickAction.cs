using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Posts a left click at client-relative coordinates of the action's Window target.</summary>
public sealed class ClickAction : IActionDefinition, IActionExecutor
{
    public const string XKey = "x";
    public const string YKey = "y";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IInputSender _sender;

    public ClickAction(IInputSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        _sender = sender;
    }

    public string TypeKey => "input.click";
    public string DisplayName => "Click";
    public string Category => "Input";
    public string Description => "Clicks at coordinates within the target window.";
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
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        if (ResolveWindow(context) is not IntPtr hwnd || hwnd == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Click requires a resolved Window target (HWND)."));
        }

        var x = ConfigValues.GetInt(context.Action.Config, XKey);
        var y = ConfigValues.GetInt(context.Action.Config, YKey);
        _sender.Click(hwnd, x, y);

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
