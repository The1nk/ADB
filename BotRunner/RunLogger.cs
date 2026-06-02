using System.Text.Json;
using System.Text.Json.Serialization;
using AdbCore.Execution;

namespace BotRunner;

/// <summary>Writes JSON-lines log records to stdout and a file, filtered by minimum level.</summary>
public sealed class RunLogger
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly TextWriter _stdout;
    private readonly TextWriter _file;
    private readonly LogLevel _minLevel;
    private readonly object _writeLock = new();

    public RunLogger(TextWriter stdout, TextWriter file, LogLevel minLevel)
    {
        _stdout = stdout;
        _file = file;
        _minLevel = minLevel;
    }

    public void RunStart(string botName)
        => Write(LogLevel.Info, new LogEntry { Event = "run-start", Bot = botName });

    public void ActionExecuted(ExecutionProgress p)
        => Write(p.Success ? LogLevel.Info : LogLevel.Error, new LogEntry
        {
            Event = "action",
            ActionId = p.ActionId.ToString(),
            Label = p.ActionLabel,
            TypeKey = p.TypeKey,
            Success = p.Success,
            Error = p.ErrorMessage,
        });

    public void Message(string text)
        => Write(LogLevel.Info, new LogEntry { Event = "log", Message = text });

    public void RunEnd(ExecutionResult r)
        => Write(LogLevel.Info, new LogEntry
        {
            Event = "run-end",
            Success = r.Success,
            ActionsExecuted = r.ActionsExecuted,
            Error = r.ErrorMessage,
        });

    private void Write(LogLevel level, LogEntry entry)
    {
        if (level < _minLevel)
        {
            return;
        }

        entry.Ts = DateTime.UtcNow.ToString("o");
        entry.Level = level.ToString().ToLowerInvariant();

        var line = JsonSerializer.Serialize(entry, Json);

        // Parallel branches can log concurrently; keep each record's stdout+file writes atomic.
        lock (_writeLock)
        {
            _stdout.WriteLine(line);
            _file.WriteLine(line);
        }
    }
}
