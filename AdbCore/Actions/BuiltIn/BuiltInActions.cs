using AdbCore.Execution;
using AdbCore.Input;

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
        Add(new SetVariableAction(), definitions, executors);
        Add(new CommentAction(), definitions, executors);

        // Input actions share one resolver: SendInput (foreground, default) + PostMessage (background, opt-in per node).
        var inputSenders = new InputSenderResolver(new Win32SendInputSender(), new Win32PostMessageSender());
        Add(new ClickAction(inputSenders), definitions, executors);
        Add(new RightClickAction(inputSenders), definitions, executors);
        Add(new DoubleClickAction(inputSenders), definitions, executors);
        Add(new MouseMoveAction(inputSenders), definitions, executors);
        Add(new TypeTextAction(inputSenders), definitions, executors);
        Add(new KeyPressAction(inputSenders), definitions, executors);

        // Loop is engine-native: register its definition only (no executor).
        definitions.Register(new LoopAction());

        // Run Parallel and Join are engine-native: register their definitions only (no executors).
        definitions.Register(new RunParallelAction());
        definitions.Register(new JoinAction());
    }

    private static void Add<T>(T action, ActionRegistry definitions, ActionExecutorRegistry executors)
        where T : IActionDefinition, IActionExecutor
    {
        definitions.Register(action);
        executors.Register(action);
    }
}
