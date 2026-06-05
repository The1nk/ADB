using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Screen;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Shared base for Screen OCR actions: resolves the target HWND, captures the client area (Auto),
/// and runs OCR via <see cref="OcrCore"/>. Exposes the shared ROI fields (no Capture Method field).</summary>
public abstract class ScreenOcrActionBase : IActionDefinition, IActionExecutor
{
    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";
    public const string TextKey = OcrCore.TextKey;
    public const string ResultVarKey = OcrCore.ResultVarKey;
    public const string MinConfidenceKey = OcrCore.MinConfidenceKey;

    private readonly IWindowCapture _capture;
    private List<ConfigField>? _configFields;

    protected ScreenOcrActionBase(IWindowCapture capture, IOcrEngine ocr)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(ocr);
        _capture = capture;
        Ocr = ocr;
    }

    protected IOcrEngine Ocr { get; }

    public abstract string TypeKey { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public string Category => "Screen";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public abstract List<PortDefinition> OutputPorts { get; }
    public virtual bool SupportsRetry => true;

    protected abstract IEnumerable<ConfigField> ActionConfigFields { get; }

    public List<ConfigField> ConfigFields => _configFields ??= [.. ActionConfigFields, .. TemplateMatchCore.RegionFields()];

    public abstract Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct);

    /// <summary>Resolves the action's target HWND (explicit TargetId or the sole target).</summary>
    protected static IntPtr? ResolveWindow(ActionExecutionContext context)
        => TargetResolution.ResolveHandle<IntPtr>(context);

    /// <summary>Captures the client area (Auto) and OCRs the configured region (full-frame word coords).</summary>
    protected OcrResult RecognizeWindow(ActionExecutionContext context, IntPtr hwnd)
    {
        using var shot = _capture.Capture(hwnd, ScreenCaptureMethod.Auto);
        return OcrCore.RecognizeRegion(shot, context.Action.Config, Ocr);
    }

    protected static ConfigField TextField() => OcrCore.TextField();
    protected static ConfigField ResultVarField(string def) => OcrCore.ResultVarField(def);
    protected static ConfigField MinConfidenceField() => OcrCore.MinConfidenceField();
}
