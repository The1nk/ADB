using System.Text.Json;

namespace BotBuilder.Core.Integration;

/// <summary>Parses one runner JSON-lines record into a <see cref="RunLogEntry"/>. Tolerant: a blank or
/// non-JSON line becomes an <see cref="RunLogKind.Unparsed"/> entry rather than throwing (the runner can
/// emit non-JSON text if it crashes).</summary>
public static class RunnerLogParser
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static RunLogEntry Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Unparsed(line);
        }

        try
        {
            var raw = JsonSerializer.Deserialize<RawLine>(line, Options);
            if (raw is null)
            {
                return Unparsed(line);
            }

            var kind = raw.Event switch
            {
                "run-start" => RunLogKind.RunStart,
                "action" => RunLogKind.Action,
                "log" => RunLogKind.Message,
                "run-end" => RunLogKind.RunEnd,
                _ => RunLogKind.Unparsed,
            };

            return new RunLogEntry(kind, raw.ActionId, raw.Label, raw.Success, raw.Error, raw.Message, line);
        }
        catch (JsonException)
        {
            return Unparsed(line);
        }
    }

    private static RunLogEntry Unparsed(string line)
        => new(RunLogKind.Unparsed, null, null, null, null, null, line);

    private sealed record RawLine
    {
        public string? Event { get; init; }
        public string? ActionId { get; init; }
        public string? Label { get; init; }
        public bool? Success { get; init; }
        public string? Error { get; init; }
        public string? Message { get; init; }
    }
}
