using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>A capture saved during the current session: its file, the confidence it was saved with, the
/// source window it came from (for re-testing), and the last re-test result (null = not yet tested).</summary>
public partial class SessionRow : ObservableObject
{
    private double _confidence;

    public SessionRow(string filePath, double confidence, IntPtr sourceHandle)
    {
        FilePath = filePath;
        _confidence = confidence;
        SourceHandle = sourceHandle;
    }

    public string FilePath { get; }
    public IntPtr SourceHandle { get; }
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>The saved confidence; updatable when the template is re-edited.</summary>
    public double Confidence
    {
        get => _confidence;
        set => SetProperty(ref _confidence, value);
    }

    /// <summary>Last re-test result: null = untested, true = matched at <see cref="Confidence"/>, false = not.</summary>
    [ObservableProperty] private bool? _lastRetestMatched;
}
