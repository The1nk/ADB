namespace AdbCore.Screen;

/// <summary>How <see cref="IWindowCapture"/> grabs the target window.</summary>
public enum ScreenCaptureMethod
{
    /// <summary>PrintWindow (works for non-foreground/standard apps), falling back to screen BitBlt on a blank frame.</summary>
    Auto,
    /// <summary>Force screen-region BitBlt (captures visible pixels incl. GPU/DirectX; window must be unoccluded).</summary>
    BitBlt,
}
