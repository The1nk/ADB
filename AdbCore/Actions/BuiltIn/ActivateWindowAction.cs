using AdbCore.Execution;
using AdbCore.Window;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Brings the target window to the foreground (restoring it if minimized). Resolves the window the
/// same way the Screen/Input actions do (explicit TargetId or the lone Window target). No window -> onFailure.</summary>
public sealed class ActivateWindowAction : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    private readonly IWindowActivator _activator;
    public ActivateWindowAction(IWindowActivator activator)
    {
        ArgumentNullException.ThrowIfNull(activator);
        _activator = activator;
    }

    public string TypeKey => "window.activate";
    public string DisplayName => "Activate Window";
    public string Category => "Window";
    public string Description => "Brings the target window to the foreground (restoring it if minimized).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handle = TargetResolution.ResolveHandle<IntPtr>(context);
        if (handle == IntPtr.Zero)
        {
            return Task.FromResult(ActionResult.Fail("Activate Window requires a window target."));
        }

        _activator.Activate(handle);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
