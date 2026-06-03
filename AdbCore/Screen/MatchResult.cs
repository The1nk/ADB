namespace AdbCore.Screen;

/// <summary>A template match in haystack (window-client) pixels. <see cref="X"/>,<see cref="Y"/> is the
/// top-left of the matched region; <see cref="Score"/> is the 0–1 match confidence.</summary>
public readonly record struct MatchResult(int X, int Y, int Width, int Height, double Score);
