namespace BotBuilder.Core.Integration;

/// <summary>The kind of a parsed runner log line.</summary>
public enum RunLogKind { RunStart, Action, Message, RunEnd, Unparsed }

/// <summary>A parsed runner JSON-lines record in display form.</summary>
public sealed record RunLogEntry(
    RunLogKind Kind,
    string? ActionId,
    string? Label,
    bool? Success,
    string? Error,
    string? Message,
    string Raw)
{
    /// <summary>A friendly one-line rendering for the log panel.</summary>
    public string Display => Kind switch
    {
        RunLogKind.RunStart => "▶ run started",
        RunLogKind.Action => (Success == true ? "✓ " : "✗ ")
                             + (Label ?? ActionId ?? "action")
                             + (Success == false && !string.IsNullOrEmpty(Error) ? $": {Error}" : string.Empty),
        RunLogKind.Message => Message ?? string.Empty,
        RunLogKind.RunEnd => Success == true
            ? "■ run succeeded"
            : "■ run failed" + (!string.IsNullOrEmpty(Error) ? $": {Error}" : string.Empty),
        _ => Raw,
    };
}
