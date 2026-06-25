namespace BotCapture.Core;

/// <summary>A picker list row: a capture source plus its display fields and an optional PNG thumbnail
/// (null when the thumbnail capture failed).</summary>
public sealed record CaptureSourceRow(ICaptureSource Source, string Label, string SubLabel, byte[]? ThumbnailPng);
