using AdbCore.Execution;
using AdbCore.Scripting;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Runs a user Lua script (MoonSharp) with two-way access to the bot variables via <c>vars</c>, plus
/// <c>json</c> and <c>log</c>. Completes -> onSuccess; a Lua error (or <c>error(...)</c>) -> onFailure with the
/// message.</summary>
public sealed class RunLuaScriptAction : IActionDefinition, IActionExecutor
{
    public const string ScriptKey = "script";
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public string TypeKey => "scripting.runLua";
    public string DisplayName => "Run Lua Script";
    public string Category => "Scripting";
    public string Description => "Runs a Lua script with access to the bot variables (vars), json, and log.";

    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };

    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = ScriptKey, Label = "Lua Script", Type = ConfigFieldType.MultilineString, DefaultValue = "" },
    };

    public bool SupportsRetry => true;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var scriptText = ConfigValues.GetString(context.Action.Config, ScriptKey);

        var host = new LuaScriptHost(context.Log);
        var result = host.Run(scriptText, context.Context.Variables, ct);

        return Task.FromResult(result.Success
            ? ActionResult.Ok(SuccessPort)
            : ActionResult.Fail($"Run Lua Script: {result.Error}"));
    }
}
