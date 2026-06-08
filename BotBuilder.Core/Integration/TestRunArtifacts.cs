using System.IO;

namespace BotBuilder.Core.Integration;

/// <summary>Builds the temp <c>.bot</c> path a Test Run serializes to, sanitising the (free-form) bot name
/// into a valid filename. Pure for testing; the runtime caller supplies <c>Path.GetTempPath()</c> as the
/// root and ensures the directory exists.</summary>
public static class TestRunArtifacts
{
    private const string SubdirectoryName = "adb-testrun";

    /// <summary>Strips characters invalid in a filename from <paramref name="botName"/>, falling back to
    /// <c>"bot"</c> when nothing usable remains.</summary>
    public static string SafeFileName(string botName)
    {
        var safe = string.Concat(botName.Split(Path.GetInvalidFileNameChars()));
        return string.IsNullOrWhiteSpace(safe) ? "bot" : safe;
    }

    /// <summary>The temp <c>.bot</c> path for a Test Run:
    /// <paramref name="tempRoot"/>/adb-testrun/&lt;safe-name&gt;.bot.</summary>
    public static string TempBotPath(string tempRoot, string botName)
        => Path.Combine(tempRoot, SubdirectoryName, $"{SafeFileName(botName)}.bot");
}
