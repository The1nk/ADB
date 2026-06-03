using System.Text;

namespace BotBuilder.Core.Integration;

/// <summary>Builds the BotRunner invocation from a bot file path and per-target selectors: an argument
/// list for <c>Process.Start</c>, and a quoted, copy-pasteable command string for the dialog preview.</summary>
public static class RunCommandBuilder
{
    public static IReadOnlyList<string> BuildArgs(
        string botPath, IReadOnlyList<(string Name, string Selector)> targets)
    {
        var args = new List<string> { "--bot", botPath };
        foreach (var (name, selector) in targets)
        {
            args.Add("--target");
            args.Add($"{name}={selector}");
        }

        return args;
    }

    public static string BuildDisplayCommand(
        string exeName, string botPath, IReadOnlyList<(string Name, string Selector)> targets)
    {
        var sb = new StringBuilder(exeName);
        sb.Append(" --bot ").Append(Quote(botPath));
        foreach (var (name, selector) in targets)
        {
            sb.Append(" --target ").Append(Quote($"{name}={selector}"));
        }

        return sb.ToString();
    }

    // Quote a token only when it contains whitespace (or is empty); escape embedded quotes.
    private static string Quote(string value)
        => value.Length == 0 || value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
}
