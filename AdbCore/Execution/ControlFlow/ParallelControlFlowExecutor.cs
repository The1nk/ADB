using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Models;

namespace AdbCore.Execution.ControlFlow;

/// <summary>Engine-native Run Parallel: runs each wired branch concurrently as a sub-walk that stops at the
/// convergent Join, aggregates the outcomes per <see cref="ParallelErrorStrategy"/>, and resumes the parent
/// walk at the Join's allSucceeded/someFailed port — or halts the run.</summary>
public sealed class ParallelControlFlowExecutor : IControlFlowExecutor
{
    public string TypeKey => RunParallelAction.RunParallelTypeKey;

    public async Task<ControlFlowResult> ExecuteAsync(ControlFlowContext context, CancellationToken ct)
    {
        var graph = context.Graph;
        var runParallel = context.Action;

        var strategy = ParseStrategy(
            ConfigValues.GetString(runParallel.Config, RunParallelAction.OnBranchFailureKey, nameof(ParallelErrorStrategy.HaltAll)));
        var branchCount = Math.Max(1,
            ConfigValues.GetInt(runParallel.Config, RunParallelAction.BranchesKey, RunParallelAction.DefaultBranchCount));

        var branchStarts = new List<BotAction>();
        for (var i = 1; i <= branchCount; i++)
        {
            var start = graph.FindNext(runParallel.Id, RunParallelAction.BranchPort(i));
            if (start is not null)
            {
                branchStarts.Add(start);
            }
        }

        if (branchStarts.Count == 0)
        {
            return ControlFlowResult.Halt(WalkOutcome.Failed("Run Parallel has no wired branch ports.", runParallel.Id));
        }

        var joinId = FindConvergentJoin(graph, branchStarts.Select(b => b.Id).ToList());
        if (joinId is null)
        {
            return ControlFlowResult.Halt(WalkOutcome.Failed("Run Parallel branches must converge on exactly one Join.", runParallel.Id));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var outcomes = new WalkOutcome[branchStarts.Count];
        WalkOutcome? branchBreakViolation = null;

        async Task RunBranchAsync(int index)
        {
            try
            {
                var outcome = await context.WalkAsync(branchStarts[index], linked.Token, joinId.Value);
                if (outcome.IsBreak)
                {
                    // A Loop-Break with no enclosing loop inside the branch has unwound to the Parallel
                    // boundary. Breaks cannot cross a Run Parallel branch; surface it as a failure rather
                    // than silently swallowing it (which would falsely report success and skip an outer loop's break).
                    outcome = WalkOutcome.Failed(
                        "Loop-Break cannot cross a Run Parallel branch boundary; place Loop-Break after the Join.",
                        branchStarts[index].Id);
                    branchBreakViolation = outcome;   // a boundary-crossing break halts the run unconditionally (see aggregation)
                    linked.Cancel();                  // stop siblings; the run is failing regardless of strategy
                }
                outcomes[index] = outcome;
                if (!outcome.Success && strategy == ParallelErrorStrategy.HaltAll)
                {
                    linked.Cancel();
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Cancelled because a sibling failed under HaltAll — not a failure of this branch itself.
                outcomes[index] = WalkOutcome.Completed();
            }
        }

        var tasks = Enumerable.Range(0, branchStarts.Count).Select(RunBranchAsync).ToArray();
        await Task.WhenAll(tasks); // a genuine user cancellation (outer ct) surfaces as OperationCanceledException

        if (branchBreakViolation is not null)
        {
            // A Loop-Break unwound to a Parallel branch boundary. This is an invalid graph, not a handleable
            // branch failure — halt regardless of ParallelErrorStrategy or someFailed wiring.
            return ControlFlowResult.Halt(branchBreakViolation);
        }

        var firstFailure = outcomes.FirstOrDefault(o => o is not null && !o.Success);
        if (firstFailure is null)
        {
            return ControlFlowResult.Continue(graph.FindNext(joinId.Value, JoinAction.AllSucceededPort));
        }

        // A branch failed. If someFailed is wired, route to it (handled) regardless of strategy.
        var someFailedNext = graph.FindNext(joinId.Value, JoinAction.SomeFailedPort);
        if (someFailedNext is not null)
        {
            return ControlFlowResult.Continue(someFailedNext);
        }

        // Unhandled failure (someFailed unwired). Continue treats it as a warning and lets the run proceed
        // (the someFailed route simply dead-ends, hence null); the Halt strategies fail the run.
        if (strategy == ParallelErrorStrategy.Continue)
        {
            return ControlFlowResult.Continue(someFailedNext); // someFailed unwired here, so next is null — the walk ends.
        }

        return ControlFlowResult.Halt(WalkOutcome.Failed(firstFailure.ErrorMessage, firstFailure.FailedActionId ?? runParallel.Id));
    }

    private static ParallelErrorStrategy ParseStrategy(string value)
        => Enum.TryParse<ParallelErrorStrategy>(value, ignoreCase: true, out var s) ? s : ParallelErrorStrategy.HaltAll;

    /// <summary>Finds the single Join node all branches converge on, choosing the nearest common Join when more
    /// than one is reachable from every branch. Returns null if zero, or an ambiguous tie.</summary>
    private static Guid? FindConvergentJoin(BotGraph graph, IReadOnlyList<Guid> branchStartIds)
    {
        var perBranch = branchStartIds.Select(id => JoinDistances(graph, id)).ToList();
        if (perBranch.Count == 0)
        {
            return null;
        }

        IEnumerable<Guid> common = perBranch[0].Keys;
        foreach (var map in perBranch.Skip(1))
        {
            common = common.Intersect(map.Keys);
        }

        var commonJoins = common.ToList();
        if (commonJoins.Count == 0)
        {
            return null;
        }

        if (commonJoins.Count == 1)
        {
            return commonJoins[0];
        }

        Guid? best = null;
        var bestScore = int.MaxValue;
        var tie = false;
        foreach (var join in commonJoins)
        {
            var score = perBranch.Max(map => map[join]);
            if (score < bestScore)
            {
                bestScore = score;
                best = join;
                tie = false;
            }
            else if (score == bestScore)
            {
                tie = true;
            }
        }

        return tie ? null : best;
    }

    /// <summary>BFS forward from <paramref name="startId"/> over all outgoing edges, returning the shortest
    /// distance to each reachable Join node.</summary>
    private static Dictionary<Guid, int> JoinDistances(BotGraph graph, Guid startId)
    {
        var distances = new Dictionary<Guid, int>();
        var visited = new HashSet<Guid> { startId };
        var queue = new Queue<(Guid Id, int Depth)>();
        queue.Enqueue((startId, 0));

        while (queue.Count > 0)
        {
            var (id, depth) = queue.Dequeue();
            var node = graph.Find(id);
            if (node is not null && node.TypeKey == JoinAction.JoinTypeKey && !distances.ContainsKey(id))
            {
                distances[id] = depth;
            }

            foreach (var edge in graph.Outgoing(id))
            {
                if (visited.Add(edge.TargetActionId))
                {
                    queue.Enqueue((edge.TargetActionId, depth + 1));
                }
            }
        }

        return distances;
    }
}
