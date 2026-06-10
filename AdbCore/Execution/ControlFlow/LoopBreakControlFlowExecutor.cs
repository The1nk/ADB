using AdbCore.Actions.BuiltIn;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Loop-Break: signals the current sub-walk to unwind to the innermost enclosing loop,
/// which consumes the break and resumes at its Done port.</summary>
public sealed class LoopBreakControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => LoopBreakAction.LoopBreakTypeKey;

    public Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ControlFlowResult.Break());
    }
}
