using AdbCore.Execution;

namespace AdbCore.Tests.Execution;

/// <summary>Test double executor with a configurable TypeKey and result.</summary>
internal sealed class FakeExecutor : IActionExecutor
{
    public required string TypeKey { get; init; }
    public Func<ActionExecutionContext, ActionResult> Behavior { get; init; } = _ => ActionResult.Ok("out");
    public int Calls { get; private set; }

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Behavior(context));
    }
}
