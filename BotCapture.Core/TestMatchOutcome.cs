using AdbCore.Screen;

namespace BotCapture.Core;

/// <summary>The result of a single Test Match run. <see cref="Matched"/> is whether the best score met the
/// chosen confidence; <see cref="Score"/>/<see cref="Location"/> describe the best match found (null when
/// the matcher couldn't run); <see cref="Error"/> is set instead when Test Match failed.</summary>
public sealed record TestMatchOutcome(bool Matched, double? Score, MatchResult? Location, string? Error);
