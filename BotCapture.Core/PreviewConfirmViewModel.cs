using System.Drawing;
using System.Drawing.Imaging;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the preview/confirm step: holds the cropped template, the chosen filename and
/// confidence, runs a live Test Match against a fresh capture of the source window, and saves the
/// template (PNG + confidence sidecar). Owns <see cref="Crop"/>.</summary>
public partial class PreviewConfirmViewModel : ObservableObject, IDisposable
{
    // Template-match score is CCOEFF_NORMED in [-1, 1]; asking for -1.0 always returns the best match,
    // so the score is shown even when it's below the user's threshold.
    private const double BestMatchFloor = -1.0;

    private readonly IntPtr _sourceHandle;
    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private readonly CaptureSaver _saver;

    public PreviewConfirmViewModel(
        Bitmap crop, IntPtr sourceHandle, IWindowCapture capture, ITemplateMatcher matcher, CaptureSaver saver)
    {
        Crop = crop;
        _sourceHandle = sourceHandle;
        _capture = capture;
        _matcher = matcher;
        _saver = saver;
        _fileName = saver.NextFileName();
    }

    /// <summary>The cropped template image to be saved.</summary>
    public Bitmap Crop { get; }

    [ObservableProperty] private TestMatchOutcome? _lastOutcome;

    private string _fileName;
    private double _confidence = 0.9;

    /// <summary>The chosen output filename (defaults to the saver's next free name).</summary>
    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    /// <summary>Match threshold in [0, 1] (default 0.9). Out-of-range assignments clamp.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>Re-captures the source window and matches the crop against it, recording the best score and
    /// whether it met <see cref="Confidence"/> into <see cref="LastOutcome"/>. Never throws.</summary>
    public void TestMatch()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"botcap_test_{Guid.NewGuid():N}.png");
        try
        {
            Crop.Save(tempPath, ImageFormat.Png);
            using var fresh = _capture.Capture(_sourceHandle, ScreenCaptureMethod.Auto);
            var best = _matcher.Match(fresh, tempPath, BestMatchFloor);
            LastOutcome = best is MatchResult m
                ? new TestMatchOutcome(m.Score >= Confidence, m.Score, m, Error: null)
                : new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: "No match could be computed.");
        }
        catch (Exception ex)
        {
            LastOutcome = new TestMatchOutcome(Matched: false, Score: null, Location: null, Error: ex.Message);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>Writes the template (PNG + confidence sidecar) under the chosen filename.</summary>
    public void Save() => _saver.Save(Crop, FileName, Confidence);

    public void Dispose() => Crop.Dispose();
}
