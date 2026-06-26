using System.Drawing;

namespace BotCapture.Core;

/// <summary>A re-capturable screenshot source — a Win32 window or a connected Android device. Lets the
/// picker, live Test Match, and standalone Retest grab a fresh frame without knowing which kind it is.</summary>
public interface ICaptureSource
{
    /// <summary>Primary display name (window title / device serial).</summary>
    string Label { get; }

    /// <summary>Secondary display line (process name / device state).</summary>
    string SubLabel { get; }

    /// <summary>Grabs a fresh frame. The caller owns and disposes the result.</summary>
    Bitmap Capture();
}
