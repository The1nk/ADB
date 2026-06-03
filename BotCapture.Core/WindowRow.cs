using AdbCore.Targets;

namespace BotCapture.Core;

/// <summary>A window-picker list row: the enumerated window plus an optional PNG thumbnail
/// (null when that window's thumbnail capture failed).</summary>
public sealed record WindowRow(WindowInfo Info, byte[]? ThumbnailPng)
{
    public string Title => Info.Title;
    public string ProcessName => Info.ProcessName;
}
