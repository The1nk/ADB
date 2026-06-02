using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Registers the built-in action set into the definition and executor registries.</summary>
public static class BuiltInActions
{
    public static void Register(ActionRegistry definitions, ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(executors);

        Add(new StartAction(), definitions, executors);
        Add(new EndAction(), definitions, executors);
        Add(new LogAction(), definitions, executors);
        Add(new DelayAction(), definitions, executors);
        Add(new BranchAction(), definitions, executors);

        // Loop is engine-native: register its definition only (no executor).
        definitions.Register(new LoopAction());
    }

    private static void Add<T>(T action, ActionRegistry definitions, ActionExecutorRegistry executors)
        where T : IActionDefinition, IActionExecutor
    {
        definitions.Register(action);
        executors.Register(action);
    }
}
