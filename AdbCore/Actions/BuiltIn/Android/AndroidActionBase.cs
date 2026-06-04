using System.Linq;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android actions: resolves the action's target to the bound
/// <see cref="IAndroidDevice"/> handle, and exposes the standard success/failure ports.</summary>
public abstract class AndroidActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Android";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public virtual List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public abstract List<ConfigField> ConfigFields { get; }
    public virtual bool SupportsRetry => false;

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>The bound Android device for this action's target (explicit TargetId, or the sole target
    /// when unset); null when the target isn't a bound Android device.</summary>
    protected static IAndroidDevice? ResolveDevice(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        ResolvedTarget? target = context.Action.TargetId is Guid id
            ? targets.TryGetValue(id, out var t) ? t : null
            : targets.Count == 1 ? targets.Values.First() : null;
        return target?.Handle as IAndroidDevice;
    }

    /// <summary>Standard "no device" failure message.</summary>
    protected ActionResult RequiresDevice() => ActionResult.Fail($"{DisplayName} requires a connected Android device target.");
}
