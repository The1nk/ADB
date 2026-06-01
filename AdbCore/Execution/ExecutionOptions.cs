namespace AdbCore.Execution;

/// <summary>Options controlling a single bot run.</summary>
public class ExecutionOptions
{
    /// <summary>Targets resolved before the run, keyed by <c>BotTarget.Id</c>.</summary>
    public IReadOnlyDictionary<Guid, ResolvedTarget> ResolvedTargets { get; set; }
        = new Dictionary<Guid, ResolvedTarget>();

    /// <summary>Sink for messages emitted by actions (e.g. the Log action). Optional.</summary>
    public Action<string>? Log { get; set; }
}
