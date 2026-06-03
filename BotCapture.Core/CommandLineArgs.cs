namespace BotCapture.Core;

/// <summary>Parsed BotCapture command-line arguments. With no args the tool runs the standalone session
/// panel; <c>--output &lt;path&gt;</c> runs integrated single-capture mode that saves to that exact path
/// then exits.</summary>
public sealed class CommandLineArgs
{
    public string? OutputPath { get; init; }

    /// <summary>True when launched to capture a single template to <see cref="OutputPath"/> then exit.</summary>
    public bool IsIntegrated => OutputPath is not null;

    public static CommandLineArgs Parse(string[] args)
    {
        string? outputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--output requires a value.");
                    }

                    outputPath = args[++i];
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new CommandLineArgs { OutputPath = outputPath };
    }
}
