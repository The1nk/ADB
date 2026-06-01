namespace BotRunner;

/// <summary>Raised for invalid CLI usage; maps to exit code 2.</summary>
public sealed class CommandLineException : Exception
{
    public CommandLineException(string message) : base(message) { }
}
