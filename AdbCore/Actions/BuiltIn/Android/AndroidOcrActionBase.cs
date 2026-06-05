using System.Drawing;
using System.IO;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Ocr;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Shared base for Android OCR actions: captures the bound device's framebuffer, decodes it, and
/// runs OCR via <see cref="OcrCore"/>. Exposes the shared ROI fields; no Capture Method field.</summary>
public abstract class AndroidOcrActionBase : AndroidActionBase
{
    private List<ConfigField>? _configFields;

    protected AndroidOcrActionBase(IOcrEngine ocr)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        Ocr = ocr;
    }

    protected IOcrEngine Ocr { get; }

    public override bool SupportsRetry => true;

    /// <summary>The action's own config fields, shown before the shared ROI fields.</summary>
    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public override List<ConfigField> ConfigFields => _configFields ??=
    [
        .. ActionConfigFields,
        .. TemplateMatchCore.RegionFields(),
    ];

    /// <summary>Captures the device framebuffer and OCRs the configured region (full-frame word coords).</summary>
    protected OcrResult RecognizeDevice(ActionExecutionContext context, IAndroidDevice device)
    {
        using var ms = new MemoryStream(device.Screenshot());
        using var frame = new Bitmap(ms);
        return OcrCore.RecognizeRegion(frame, context.Action.Config, Ocr);
    }
}
