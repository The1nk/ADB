using System.IO;
using System.Windows;
using System.Windows.Controls;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;
using BotCapture.Views;

namespace BotCapture;

public partial class MainWindow : Window
{
    private readonly IWindowCapture _capture = new Win32WindowCapture();
    private readonly ITemplateMatcher _matcher = new OpenCvSharpTemplateMatcher();

    private readonly WindowPickerViewModel _pickerVm;
    private readonly WindowPickerView _pickerView;

    private IntPtr _sourceHandle;
    private RegionSelectionViewModel? _regionVm;
    private PreviewConfirmViewModel? _confirmVm;

    public MainWindow()
    {
        InitializeComponent();

        _pickerVm = new WindowPickerViewModel(new Win32WindowEnumerator(), _capture);
        _pickerView = new WindowPickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;

        ShowPicker();
        _pickerVm.Refresh();
    }

    private void SetContent(UIElement view)
    {
        Root.Children.Clear();
        Root.Children.Add(view);
    }

    private void ShowPicker()
    {
        SetContent(_pickerView);
    }

    private void OnCaptureAccepted(object? sender, EventArgs e)
    {
        var image = _pickerVm.TakeCapturedImage();
        if (image is null || _pickerVm.SelectedWindow is null)
        {
            return;
        }

        _sourceHandle = _pickerVm.SelectedWindow.Info.Handle;
        ShowRegion(new RegionSelectionViewModel(image));
    }

    private void ShowRegion(RegionSelectionViewModel vm)
    {
        _regionVm?.Dispose();
        _regionVm = vm;

        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ShowPicker(); };
        view.Bind(vm);
        SetContent(view);
    }

    private void OnRegionConfirmed(object? sender, System.Drawing.Bitmap crop)
    {
        _confirmVm?.Dispose();
        _confirmVm = new PreviewConfirmViewModel(crop, _sourceHandle, _capture, _matcher, new CaptureSaver(SaveFolder()));

        var view = new PreviewConfirmView();
        view.Saved += (_, _) => { DisposeConfirm(); DisposeRegion(); ShowPicker(); };
        view.RetakeRequested += (_, _) => { DisposeConfirm(); if (_regionVm is not null) ReshowRegion(); };
        view.Bind(_confirmVm);
        SetContent(view);
    }

    // Retake: re-open region selection on the same source (the region VM still owns the source bitmap).
    private void ReshowRegion()
    {
        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ShowPicker(); };
        view.Bind(_regionVm!);
        SetContent(view);
    }

    private static string SaveFolder()
    {
        // M6b standalone default; M6c adds a folder picker. Use a stable, user-writable location.
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BotCapture");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void DisposeRegion()
    {
        _regionVm?.Dispose();
        _regionVm = null;
    }

    private void DisposeConfirm()
    {
        _confirmVm?.Dispose();
        _confirmVm = null;
    }
}
