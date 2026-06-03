using System.Collections.ObjectModel;
using System.Drawing;
using AdbCore.Screen;
using AdbCore.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the window-picker screen: enumerates visible windows into rows (each with a thumbnail),
/// and captures the selected window's client area. Capture failures surface as <see cref="StatusMessage"/>
/// rather than exceptions.</summary>
public partial class WindowPickerViewModel : ObservableObject
{
    private const int ThumbnailMaxDimension = 160;

    private readonly IWindowEnumerator _enumerator;
    private readonly IWindowCapture _capture;

    public WindowPickerViewModel(IWindowEnumerator enumerator, IWindowCapture capture)
    {
        _enumerator = enumerator;
        _capture = capture;
    }

    public ObservableCollection<WindowRow> Windows { get; } = new();

    [ObservableProperty] private WindowRow? _selectedWindow;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>The most recent successful client-area capture of the selected window; null until a
    /// capture succeeds. Handed off to the region-select stage (M6b).</summary>
    public Bitmap? CapturedImage { get; private set; }

    /// <summary>Re-enumerate visible windows and rebuild rows (capturing a thumbnail per row).</summary>
    public void Refresh()
    {
        StatusMessage = null;
        Windows.Clear();
        foreach (var info in _enumerator.Enumerate())
        {
            Windows.Add(new WindowRow(info, TryCaptureThumbnail(info.Handle)));
        }
    }

    /// <summary>Capture the selected window's client area into <see cref="CapturedImage"/>.
    /// Returns false (with a <see cref="StatusMessage"/>) on no selection or capture failure.</summary>
    public bool CaptureSelected()
    {
        if (SelectedWindow is null)
        {
            StatusMessage = "Select a window first.";
            return false;
        }

        try
        {
            CapturedImage?.Dispose();
            CapturedImage = _capture.Capture(SelectedWindow.Info.Handle, ScreenCaptureMethod.Auto);
            StatusMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            CapturedImage = null;
            StatusMessage = $"Couldn't capture that window: {ex.Message}";
            return false;
        }
    }

    private byte[]? TryCaptureThumbnail(IntPtr handle)
    {
        try
        {
            using var bmp = _capture.Capture(handle, ScreenCaptureMethod.Auto);
            return ThumbnailEncoder.ToPng(bmp, ThumbnailMaxDimension);
        }
        catch
        {
            return null; // window may be unrenderable; the row still shows, just without a thumbnail
        }
    }
}
