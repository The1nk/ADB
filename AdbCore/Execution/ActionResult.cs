namespace AdbCore.Execution;

/// <summary>The outcome of executing a single action.</summary>
public class ActionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The output port to follow next, e.g. "out", "onSuccess", "true". Empty = terminal.</summary>
    public string OutputPort { get; set; } = string.Empty;

    /// <summary>Optional named outputs produced by the action.</summary>
    public Dictionary<string, object> Outputs { get; set; } = new();

    public static ActionResult Ok(string outputPort) => new() { Success = true, OutputPort = outputPort };

    public static ActionResult Fail(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}
