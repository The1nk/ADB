using System.Collections.ObjectModel;
using AdbCore.Screen;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Standalone session state: the captures saved so far, the save folder, and re-testing a saved
/// template against a fresh capture of the window it came from.</summary>
public partial class SessionViewModel : ObservableObject
{
    private readonly IWindowCapture _capture;
    private readonly ITemplateMatcher _matcher;
    private string _saveFolder;

    public SessionViewModel(IWindowCapture capture, ITemplateMatcher matcher, string saveFolder)
    {
        _capture = capture;
        _matcher = matcher;
        _saveFolder = saveFolder;
    }

    public ObservableCollection<SessionRow> Rows { get; } = new();

    /// <summary>The folder new captures are saved into (changeable via the panel's Browse button).</summary>
    public string SaveFolder
    {
        get => _saveFolder;
        set => SetProperty(ref _saveFolder, value);
    }

    /// <summary>Appends a saved capture as a session row and returns it.</summary>
    public SessionRow Add(string filePath, double confidence, IntPtr sourceHandle)
    {
        var row = new SessionRow(filePath, confidence, sourceHandle);
        Rows.Add(row);
        return row;
    }

    public void Remove(SessionRow row) => Rows.Remove(row);

    /// <summary>Re-captures the row's source window and matches its saved template at the row's confidence,
    /// updating <see cref="SessionRow.LastRetestMatched"/> (true = matched). Never throws.</summary>
    public void Retest(SessionRow row)
    {
        try
        {
            using var fresh = _capture.Capture(row.SourceHandle, ScreenCaptureMethod.Auto);
            row.LastRetestMatched = _matcher.Match(fresh, row.FilePath, row.Confidence) is not null;
        }
        catch
        {
            row.LastRetestMatched = false; // missing/unreadable template or capture failure -> red
        }
    }
}
