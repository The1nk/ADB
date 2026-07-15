using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android image-matching actions: captures the bound device's framebuffer (or reads
/// a stored frame when Source = Stored), decodes it, and runs template matching via the shared
/// <see cref="TemplateMatchCore"/> so the output contract matches the Screen image actions exactly. Exposes
/// the Source + ROI fields but NO Capture Method field (the framebuffer has no BitBlt/PrintWindow variants).</summary>
public abstract class AndroidImageActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared Source + ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        // Source fields live on the base because EVERY subclass here is a reader (no writer shares this
        // base, unlike ScreenActionBase which ScreenshotAction shares — hence Screen adds them per-reader).
        .. FrameSourceConfig.Fields(),
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer (or reads a stored frame), crops to any ROI, matches the
    /// configured template, and returns the match in full-frame device-pixel coordinates (null if none ≥
    /// confidence).</summary>
    protected static MatchResult? CaptureAndMatch(ActionExecutionContext context, IAndroidDevice device, ITemplateMatcher matcher, double confidence)
    {
        using var frame = AcquireHaystack(context, device);
        return TemplateMatchCore.MatchInRegion(frame, context.Action.Config, matcher, confidence);
    }

    /// <summary>The haystack to match: a fresh device screenshot (default) or a stored
    /// <see cref="FrameSnapshot"/> when Source = Stored. Throws when the named stored frame is absent. Caller
    /// disposes.</summary>
    private static Bitmap AcquireHaystack(ActionExecutionContext context, IAndroidDevice device)
    {
        if (FrameSourceConfig.UsesStoredFrame(context.Action.Config))
        {
            var name = FrameSourceConfig.FrameNameOf(context.Action.Config);
            if (!context.Context.Frames.TryGet(name, out var snapshot) || snapshot is null)
            {
                throw new InvalidOperationException($"No stored frame named '{name}'. Add a Capture Frame action before this one.");
            }
            return snapshot.ToBitmap();
        }

        using var ms = new MemoryStream(device.Screenshot());
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded); // detached copy so the stream can be disposed safely
    }
}
