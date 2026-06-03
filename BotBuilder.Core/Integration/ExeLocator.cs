namespace BotBuilder.Core.Integration;

/// <summary>Locates a sibling executable (e.g. <c>BotRunner.exe</c>) by probing candidate paths. The
/// selection (<see cref="Locate"/>) is pure for testing; the runtime caller passes
/// <c>File.Exists</c> and <see cref="Candidates"/> built from <c>AppContext.BaseDirectory</c>.</summary>
public static class ExeLocator
{
    /// <summary>The first candidate that satisfies <paramref name="exists"/>, or null when none do.</summary>
    public static string? Locate(IEnumerable<string> candidatePaths, Func<string, bool> exists)
        => candidatePaths.FirstOrDefault(exists);

    /// <summary>Candidate paths for a sibling exe: next to the Builder (deployed), and the dev build-output
    /// sibling that shares the same <c>bin/&lt;config&gt;/&lt;tfm&gt;</c> layout under its own project folder.</summary>
    public static IReadOnlyList<string> Candidates(string baseDir, string exeFileName)
    {
        var sep = Path.DirectorySeparatorChar;
        var deployed = Path.Combine(baseDir, exeFileName);

        // Dev: ...\<root>\BotBuilder\bin\<cfg>\<tfm>\  ->  ...\<root>\<ExeProject>\bin\<cfg>\<tfm>\<exe>
        var project = Path.GetFileNameWithoutExtension(exeFileName);
        var devDir = baseDir.Replace($"{sep}BotBuilder{sep}", $"{sep}{project}{sep}");
        var dev = Path.Combine(devDir, exeFileName);

        return new[] { deployed, dev };
    }
}
