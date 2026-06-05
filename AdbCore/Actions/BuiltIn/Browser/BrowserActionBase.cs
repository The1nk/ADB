using AdbCore.Browser;
using AdbCore.Execution;
using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn.Browser;

/// <summary>Shared base for Browser actions: resolves the action's target to the bound
/// <see cref="IBrowserPage"/> handle, and exposes the standard success/failure ports.</summary>
public abstract class BrowserActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Browser";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public virtual List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public abstract List<ConfigField> ConfigFields { get; }
    public virtual bool SupportsRetry => false;

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>The bound browser page for this action's target (explicit TargetId, or the sole target when
    /// unset); null when the target isn't a bound Browser page.</summary>
    protected static IBrowserPage? ResolvePage(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IBrowserPage>(context);

    protected ActionResult RequiresPage() => ActionResult.Fail($"{DisplayName} requires a Browser target.");
}
