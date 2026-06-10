namespace AdbCore.Actions.BuiltIn;

/// <summary>Exits the innermost enclosing loop early, then the loop follows its Done port. Execution is
/// engine-native (see <c>LoopBreakControlFlowExecutor</c> / <c>BotExecutor.WalkAsync</c>); this type supplies
/// palette and properties-panel metadata only and has no executor. Terminal: one input, no outputs.</summary>
public sealed class LoopBreakAction : IActionDefinition
{
    public const string LoopBreakTypeKey = "control.loopBreak";

    public string TypeKey => LoopBreakTypeKey;
    public string DisplayName => "Loop-Break";
    public string Category => "Control Flow";
    public string Description => "Exits the innermost enclosing loop early (the loop then follows Done).";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new();   // terminal, like End
    public List<ConfigField> ConfigFields { get; } = new();
    public bool SupportsRetry => false;
}
