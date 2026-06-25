using System.Collections.ObjectModel;
using System.Drawing;
using AdbCore.Android;
using AdbCore.Screen;
using AdbCore.Targets;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BotCapture.Core;

/// <summary>Drives the source picker: lists Win32 windows or connected Android devices (selectable via
/// <see cref="Kind"/>) as <see cref="CaptureSourceRow"/>s with thumbnails, and captures the selected
/// source. Capture failures surface as <see cref="StatusMessage"/>; an unavailable Android backend
/// surfaces as <see cref="UnavailableReason"/> — neither throws.</summary>
public partial class SourcePickerViewModel : ObservableObject
{
    private const int ThumbnailMaxDimension = 160;

    private readonly IWindowEnumerator _windows;
    private readonly IWindowCapture _windowCapture;
    private readonly IAdbDevices _devices;
    private readonly IAndroidDeviceConnector _connector;

    public SourcePickerViewModel(
        IWindowEnumerator windows, IWindowCapture windowCapture,
        IAdbDevices devices, IAndroidDeviceConnector connector)
    {
        _windows = windows;
        _windowCapture = windowCapture;
        _devices = devices;
        _connector = connector;
    }

    public ObservableCollection<CaptureSourceRow> Sources { get; } = new();

    [ObservableProperty] private CaptureSourceRow? _selectedSource;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Why the Android source can't be listed (adb missing / no devices); null when listing windows
    /// or when devices are present.</summary>
    [ObservableProperty] private string? _unavailableReason;

    /// <summary>Which source kind is shown. Changing it rebuilds the list.</summary>
    [ObservableProperty] private SourceKind _kind;

    /// <summary>The most recent successful capture; null until a capture succeeds.</summary>
    [ObservableProperty] private Bitmap? _capturedImage;

    partial void OnKindChanged(SourceKind value) => Refresh();
    partial void OnCapturedImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCapture));

    /// <summary>Whether a capture is available to advance with.</summary>
    public bool HasCapture => CapturedImage is not null;

    /// <summary>The selected row's underlying source (handed to the confirm/session step), or null.</summary>
    public ICaptureSource? SelectedCaptureSource => SelectedSource?.Source;

    /// <summary>Rebuild the source list for the current <see cref="Kind"/>, clearing any prior capture so the
    /// picker doesn't show a stale preview after a refresh.</summary>
    public void Refresh()
    {
        StatusMessage = null;
        UnavailableReason = null;
        CapturedImage?.Dispose();
        CapturedImage = null;
        Sources.Clear();

        if (Kind == SourceKind.Window)
        {
            RefreshWindows();
        }
        else
        {
            RefreshAndroid();
        }
    }

    private void RefreshWindows()
    {
        foreach (var info in _windows.Enumerate())
        {
            var source = new WindowCaptureSource(info, _windowCapture);
            Sources.Add(new CaptureSourceRow(source, info.Title, info.ProcessName, TryThumbnail(source)));
        }
    }

    private void RefreshAndroid()
    {
        IReadOnlyList<AdbDeviceInfo> list;
        try
        {
            list = _devices.List();
        }
        catch (Exception ex)
        {
            UnavailableReason = $"adb not found on PATH — install Android platform-tools. ({ex.Message})";
            return;
        }

        foreach (var device in list)
        {
            ICaptureSource source;
            try
            {
                source = new AndroidCaptureSource(device.Serial, device.State, _connector.Connect(device.Serial));
            }
            catch
            {
                continue; // couldn't bind this device right now; skip it
            }
            Sources.Add(new CaptureSourceRow(source, device.Serial, device.State, TryThumbnail(source)));
        }

        if (Sources.Count == 0)
        {
            UnavailableReason = "No devices connected — run `adb devices`, then Refresh.";
        }
    }

    private static byte[]? TryThumbnail(ICaptureSource source)
    {
        try
        {
            using var bmp = source.Capture();
            return ThumbnailEncoder.ToPng(bmp, ThumbnailMaxDimension);
        }
        catch
        {
            return null; // unrenderable/offline source; the row still shows, just without a thumbnail
        }
    }

    /// <summary>Capture the selected source into <see cref="CapturedImage"/>. Returns false (with a
    /// <see cref="StatusMessage"/>) on no selection or capture failure; a failed capture leaves any prior
    /// <see cref="CapturedImage"/> untouched.</summary>
    public bool CaptureSelected()
    {
        if (SelectedSource is null)
        {
            StatusMessage = "Select a source first.";
            return false;
        }

        try
        {
            var captured = SelectedSource.Source.Capture();
            CapturedImage?.Dispose();
            CapturedImage = captured;
            StatusMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't capture that source: {ex.Message}";
            return false;
        }
    }

    /// <summary>Hands the current capture to the next step, transferring ownership: returns the bitmap and
    /// clears the field WITHOUT disposing it (the caller now owns and disposes it).</summary>
    public Bitmap? TakeCapturedImage()
    {
        var image = CapturedImage;
        CapturedImage = null; // relinquish without dispose; ownership moves to the caller
        return image;
    }
}
