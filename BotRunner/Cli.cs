namespace BotRunner;

/// <summary>Testable command-line entry point: parses args, runs, and maps exceptions to exit codes.</summary>
public static class Cli
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        try
        {
            var parsed = CommandLineArgs.Parse(args);
            return await new RunnerApp().RunAsync(parsed, stdout, ct);
        }
        catch (CommandLineException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Unexpected error: {ex.Message}");
            return 1;
        }
    }
}
