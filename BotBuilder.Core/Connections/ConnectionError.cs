namespace BotBuilder.Core.Connections;

/// <summary>Why a proposed connection was rejected (or <see cref="None"/> if allowed).</summary>
public enum ConnectionError
{
    None,
    NotOutputToInput,
    SelfConnection,
    Duplicate,
    WouldCreateCycle,
}
