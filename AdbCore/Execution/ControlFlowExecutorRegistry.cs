using AdbCore.Execution.ControlFlow;

namespace AdbCore.Execution;

/// <summary>Catalogue of engine-native control-flow executors, keyed by
/// <see cref="IControlFlowExecutor.TypeKey"/>. Mirrors <see cref="ActionExecutorRegistry"/>.</summary>
public sealed class ControlFlowExecutorRegistry
{
    private readonly Dictionary<string, IControlFlowExecutor> _byKey = new(StringComparer.Ordinal);

    public int Count => _byKey.Count;

    public void Register(IControlFlowExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        if (!_byKey.TryAdd(executor.TypeKey, executor))
        {
            throw new InvalidOperationException(
                $"A control-flow executor with TypeKey '{executor.TypeKey}' is already registered.");
        }
    }

    public bool TryGet(string typeKey, out IControlFlowExecutor? executor)
        => _byKey.TryGetValue(typeKey, out executor);

    /// <summary>The default set wired into <see cref="BotExecutor"/>: Loop and Run Parallel.</summary>
    public static ControlFlowExecutorRegistry CreateDefault()
    {
        var registry = new ControlFlowExecutorRegistry();
        registry.Register(new LoopControlFlowExecutor());
        registry.Register(new ParallelControlFlowExecutor());
        return registry;
    }
}
