using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android image-matching actions: captures the bound device's framebuffer,
/// decodes it, and runs template matching via the shared <see cref="TemplateMatchCore"/> so the output
/// contract matches the Screen image actions exactly. Exposes the ROI fields but NO Capture Method field
/// (the framebuffer has no BitBlt/PrintWindow variants).</summary>
public abstract class AndroidImageActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer, crops to any ROI, matches the template, and returns the
    /// match in full-frame device-pixel coordinates (null if none ≥ confidence).</summary>
    protected static MatchResult? CaptureAndMatch(ActionExecutionContext context, IAndroidDevice device, ITemplateMatcher matcher, string templatePath, double confidence)
    {
        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        return TemplateMatchCore.MatchInRegion(frame, context.Action.Config, matcher, templatePath, confidence);
    }
}
