using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Serialization;
using AdbCore.Targets;

namespace BotRunner;

/// <summary>Orchestrates a single bot run: load, resolve targets, execute, log, and produce an exit code.</summary>
public sealed class RunnerApp
{
    /// <summary>Runs the bot. Returns 0 on success, 1 on run failure.
    /// Throws <see cref="CommandLineException"/> for usage problems (caller maps to exit 2).</summary>
    public async Task<int> RunAsync(CommandLineArgs args, TextWriter stdout, CancellationToken ct)
    {
        if (!File.Exists(args.BotPath))
        {
            throw new CommandLineException($"Bot file not found: {args.BotPath}");
        }

        var bot = new BotSerializer().Load(args.BotPath);

        // Throws CommandLineException before any file is opened if a declared target is unmatched.
        var resolvedTargets = TargetResolver.Resolve(bot, args.Targets);

        IDisposable? builtInResources = null;
        try
        {
            // Resolve Window target selectors to live HWNDs before execution (Input/Screen need them).
            WindowTargetBinder.Bind(resolvedTargets, new Win32WindowResolver());

            // Resolve Android target selectors to bound IAndroidDevice handles before execution.
            AndroidTargetBinder.Bind(resolvedTargets);

            // Launch a Playwright browser per Browser target and store the IBrowserPage as the handle.
            await BrowserTargetBinder.BindAsync(resolvedTargets);

            var logPath = args.LogFile ?? Path.ChangeExtension(args.BotPath, ".log");
            using var fileWriter = new StreamWriter(logPath, append: false);
            var logger = new RunLogger(stdout, fileWriter, args.LogLevel);

            var definitions = new ActionRegistry();
            var executors = new ActionExecutorRegistry();
            builtInResources = BuiltInActions.Register(definitions, executors);

            var options = new ExecutionOptions
            {
                ResolvedTargets = resolvedTargets,
                Log = logger.Message,
            };
            var progress = new InlineProgress<ExecutionProgress>(logger.ActionExecuted);

            logger.RunStart(bot.Name);
            var result = await new BotExecutor(executors).RunAsync(bot, options, progress, ct);
            logger.RunEnd(result);

            return result.Success ? 0 : 1;
        }
        finally
        {
            await DisposeTargetHandlesAsync(resolvedTargets);
            builtInResources?.Dispose();
        }
    }

    private static async Task DisposeTargetHandlesAsync(IReadOnlyDictionary<Guid, ResolvedTarget> targets)
    {
        // Best-effort per handle: a handle that fails to dispose (e.g. a browser the user closed mid-run)
        // must not prevent the remaining handles from being cleaned up.
        foreach (var target in targets.Values)
        {
            try
            {
                switch (target.Handle)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync();
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch
            {
                // Swallow: run teardown should never throw over a handle that's already gone.
            }
        }
    }

    /// <summary>Synchronous <see cref="IProgress{T}"/> so log lines are written in deterministic order.</summary>
    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public InlineProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
