using AdbCore.Models;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Fans out its wired branch ports as concurrent sub-walks that converge on a Join.
/// Execution is engine-native (see <c>BotExecutor.ExecuteParallelAsync</c>); this type supplies
/// palette/properties metadata only and has no executor.</summary>
public sealed class RunParallelAction : IActionDefinition
{
    public const string RunParallelTypeKey = "control.runParallel";
    public const string BranchesKey = "branches";
    public const string OnBranchFailureKey = "onBranchFailure";
    public const int DefaultBranchCount = 2;

    private const string BranchPortPrefix = "branch";

    /// <summary>The output port name for the 1-based branch index, e.g. <c>branch1</c>.</summary>
    public static string BranchPort(int oneBasedIndex) => $"{BranchPortPrefix}{oneBasedIndex}";

    public string TypeKey => RunParallelTypeKey;
    public string DisplayName => "Run Parallel";
    public string Category => "Control Flow";
    public string Description => "Runs each wired branch concurrently; branches converge on a Join.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = BranchPort(1), Label = "Branch 1" },
        new PortDefinition { Name = BranchPort(2), Label = "Branch 2" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = BranchesKey, Label = "Branches", Type = ConfigFieldType.Number, DefaultValue = DefaultBranchCount },
        new ConfigField
        {
            Key = OnBranchFailureKey,
            Label = "On Branch Failure",
            Type = ConfigFieldType.Enum,
            DefaultValue = nameof(ParallelErrorStrategy.HaltAll),
            Options = new()
            {
                nameof(ParallelErrorStrategy.HaltAll),
                nameof(ParallelErrorStrategy.WaitThenHalt),
                nameof(ParallelErrorStrategy.Continue),
            },
        },
    };
    public bool SupportsRetry => false;
}
