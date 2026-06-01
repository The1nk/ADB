namespace BotRunner;

/// <summary>Parsed command-line arguments for the runner.</summary>
public sealed class CommandLineArgs
{
    public string BotPath { get; init; } = string.Empty;
    public Dictionary<string, string> Targets { get; init; } = new(StringComparer.Ordinal);
    public LogLevel LogLevel { get; init; } = LogLevel.Info;
    public string? LogFile { get; init; }

    public static CommandLineArgs Parse(string[] args)
    {
        string? botPath = null;
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var logLevel = LogLevel.Info;
        string? logFile = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bot":
                    botPath = RequireValue(args, ref i, "--bot");
                    break;

                case "--target":
                    var target = RequireValue(args, ref i, "--target");
                    var eq = target.IndexOf('=');
                    if (eq <= 0)
                    {
                        throw new CommandLineException(
                            $"--target must be in the form Name=selector, got '{target}'.");
                    }
                    targets[target[..eq]] = target[(eq + 1)..];
                    break;

                case "--log-level":
                    var level = RequireValue(args, ref i, "--log-level");
                    if (!Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed))
                    {
                        throw new CommandLineException(
                            $"Unknown --log-level '{level}'. Use debug, info, warn, or error.");
                    }
                    logLevel = parsed;
                    break;

                case "--log-file":
                    logFile = RequireValue(args, ref i, "--log-file");
                    break;

                default:
                    throw new CommandLineException($"Unknown argument '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(botPath))
        {
            throw new CommandLineException("--bot <path> is required.");
        }

        return new CommandLineArgs
        {
            BotPath = botPath,
            Targets = targets,
            LogLevel = logLevel,
            LogFile = logFile,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new CommandLineException($"{flag} requires a value.");
        }
        return args[++i];
    }
}
