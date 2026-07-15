using System;
using System.Threading;
using System.Threading.Tasks;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Execution;

public class ParallelSinkSafetyTests
{
    private static BotAction Node(string typeKey, out Guid id)
    {
        id = Guid.NewGuid();
        return new BotAction { Id = id, TypeKey = typeKey, Label = typeKey };
    }

    private static ActionConnection Edge(Guid from, string port, Guid to)
        => new() { Id = Guid.NewGuid(), SourceActionId = from, SourcePort = port, TargetActionId = to, TargetPort = "in" };

    private static BotAction RunParallel(out Guid id, int branches)
    {
        id = Guid.NewGuid();
        var n = new BotAction { Id = id, TypeKey = RunParallelAction.RunParallelTypeKey, Label = "rp" };
        n.Config[RunParallelAction.BranchesKey] = branches;
        n.Config[RunParallelAction.OnBranchFailureKey] = ParallelErrorStrategy.HaltAll.ToString();
        return n;
    }

    /// <summary>Detects whether it was ever entered by two threads at once, and counts calls non-atomically so a
    /// serialization failure would also drop updates.</summary>
    private sealed class OverlapDetector
    {
        private int _inside;
        private int _count;
        public bool Overlapped { get; private set; }
        public int Count => _count;

        public void Enter()
        {
            if (Interlocked.Increment(ref _inside) != 1) { Overlapped = true; }
            var c = _count;          // deliberately non-atomic read-modify-write
            Thread.SpinWait(200);
            _count = c + 1;
            Interlocked.Decrement(ref _inside);
        }
    }

    private sealed class LoggingExecutor : IActionExecutor
    {
        public required string TypeKey { get; init; }
        public required int Times { get; init; }
        public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
        {
            for (var i = 0; i < Times; i++) { context.Log($"{TypeKey}:{i}"); }
            return Task.FromResult(ActionResult.Ok("out"));
        }
    }

    private static Bot FanOut(int branches)
    {
        var rp = RunParallel(out var rpId, branches);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var bot = new Bot { Name = "sink-safety" };
        bot.Actions.Add(rp);
        for (var i = 0; i < branches; i++)
        {
            var leaf = Node($"leaf{i}", out var leafId);
            bot.Actions.Add(leaf);
            bot.Connections.Add(Edge(rpId, RunParallelAction.BranchPort(i + 1), leafId));
            bot.Connections.Add(Edge(leafId, "out", joinId));
        }
        bot.Actions.Add(join);
        return bot;
    }

    /// <summary>Each branch is a CHAIN of <paramref name="actionsPerBranch"/> actions (not a single leaf), so
    /// Progress.Report fires once per action per branch — enough concurrent pressure across branches for a
    /// progress-sink race to actually surface.</summary>
    private static Bot FanOutChain(int branches, int actionsPerBranch)
    {
        var rp = RunParallel(out var rpId, branches);
        var join = Node(JoinAction.JoinTypeKey, out var joinId);
        var bot = new Bot { Name = "sink-safety-chain" };
        bot.Actions.Add(rp);
        for (var b = 0; b < branches; b++)
        {
            Guid prevId = default;
            for (var a = 0; a < actionsPerBranch; a++)
            {
                var node = Node($"b{b}n{a}", out var nodeId);
                bot.Actions.Add(node);
                bot.Connections.Add(a == 0
                    ? Edge(rpId, RunParallelAction.BranchPort(b + 1), nodeId)
                    : Edge(prevId, "out", nodeId));
                prevId = nodeId;
            }
            bot.Connections.Add(Edge(prevId, "out", joinId));
        }
        bot.Actions.Add(join);
        return bot;
    }

    [Fact(Timeout = 20000)]
    public async Task ConcurrentBranches_LogSink_IsSerialized_NoOverlapNoLostMessages()
    {
        const int branches = 4;
        const int perBranch = 200;
        var bot = FanOut(branches);

        var detector = new OverlapDetector();
        var registry = new ActionExecutorRegistry();
        for (var i = 0; i < branches; i++)
        {
            registry.Register(new LoggingExecutor { TypeKey = $"leaf{i}", Times = perBranch });
        }

        var options = new ExecutionOptions { Log = _ => detector.Enter() };
        var result = await new BotExecutor(registry).RunAsync(bot, options, null, default);

        Assert.True(result.Success);
        Assert.False(detector.Overlapped);
        Assert.Equal(branches * perBranch, detector.Count);
    }

    private sealed class DelegateProgress : IProgress<ExecutionProgress>
    {
        private readonly Action<ExecutionProgress> _onReport;
        public DelegateProgress(Action<ExecutionProgress> onReport) => _onReport = onReport;
        public void Report(ExecutionProgress value) => _onReport(value);
    }

    [Fact(Timeout = 20000)]
    public async Task ConcurrentBranches_ProgressSink_IsSerialized_NoOverlap()
    {
        const int branches = 4;
        const int actionsPerBranch = 40;
        var bot = FanOutChain(branches, actionsPerBranch);

        var detector = new OverlapDetector();
        var registry = new ActionExecutorRegistry();
        for (var b = 0; b < branches; b++)
            for (var a = 0; a < actionsPerBranch; a++)
                registry.Register(new FakeExecutor { TypeKey = $"b{b}n{a}", Behavior = c => ActionResult.Ok("out") });

        IProgress<ExecutionProgress> progress = new DelegateProgress(_ => detector.Enter());
        var result = await new BotExecutor(registry).RunAsync(bot, new ExecutionOptions(), progress, default);

        Assert.True(result.Success);
        Assert.False(detector.Overlapped);
        Assert.Equal(branches * actionsPerBranch, detector.Count); // one Progress.Report per executed action, no losses
    }
}
