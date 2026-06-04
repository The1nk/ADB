namespace BotBuilder.Core.Picker;

/// <summary>One coordinate the picker collects for an action: the config field keys its X and Y are
/// written to, plus a user-facing label (e.g. "Start", "End", "Target").</summary>
public sealed record CoordinatePoint(string XKey, string YKey, string Label);
