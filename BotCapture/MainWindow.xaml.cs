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

    private readonly SessionViewModel _sessionVm;
    private readonly SessionView _sessionView;

    private IntPtr _sourceHandle;
    private RegionSelectionViewModel? _regionVm;
    private PreviewConfirmViewModel? _confirmVm;
    private SessionRow? _editingRow;          // non-null while re-editing an existing row
    private readonly string? _outputPath;     // null in standalone; set for integrated mode

    public MainWindow(string? outputPath = null)
    {
        InitializeComponent();
        _outputPath = outputPath;

        _pickerVm = new WindowPickerViewModel(new Win32WindowEnumerator(), _capture);
        _pickerView = new WindowPickerView { DataContext = _pickerVm };
        _pickerView.CaptureAccepted += OnCaptureAccepted;

        _sessionVm = new SessionViewModel(_capture, _matcher, DefaultFolder());
        _sessionView = new SessionView { DataContext = _sessionVm };
        // Session events are only reachable in standalone mode (integrated mode never shows the session
        // panel). If re-edit is ever wired into integrated mode, revisit the FileName ordering in ShowConfirm.
        _sessionView.NewCaptureRequested += (_, _) => StartNewCapture();
        _sessionView.RetestRequested += (_, row) => _sessionVm.Retest(row);
        _sessionView.DeleteRequested += (_, row) => _sessionVm.Remove(row);
        _sessionView.ReEditRequested += (_, row) => StartReEdit(row);
        _sessionView.BrowseFolderRequested += (_, _) => BrowseFolder();

        if (_outputPath is not null)
        {
            // Integrated: fail-by-default until a save succeeds, then jump straight into the capture flow.
            Environment.ExitCode = 1;
            _pickerVm.Refresh();
            SetContent(_pickerView);
        }
        else
        {
            ShowSession();
        }
    }

    private void SetContent(UIElement view)
    {
        Root.Children.Clear();
        Root.Children.Add(view);
    }

    private void ShowSession() => SetContent(_sessionView);

    private void StartNewCapture()
    {
        _editingRow = null;
        _pickerVm.Refresh();
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
        SetContent(BuildRegionView(vm));
    }

    private RegionSelectView BuildRegionView(RegionSelectionViewModel vm)
    {
        var view = new RegionSelectView();
        view.RegionConfirmed += OnRegionConfirmed;
        view.BackRequested += (_, _) => { DisposeRegion(); ReturnHome(); };
        view.Bind(vm);
        return view;
    }

    // Home is the session panel in standalone; in integrated (--output) mode it's the picker, so
    // backing out of region/confirm lets the user re-pick a window rather than land on a stray session.
    private void ReturnHome()
    {
        if (_outputPath is not null)
        {
            SetContent(_pickerView);
        }
        else
        {
            ShowSession();
        }
    }

    private void OnRegionConfirmed(object? sender, System.Drawing.Bitmap crop)
        => ShowConfirm(crop, _sourceHandle, fileName: null, confidence: null);

    private void ShowConfirm(System.Drawing.Bitmap crop, IntPtr sourceHandle, string? fileName, double? confidence)
    {
        _confirmVm?.Dispose();

        var saveFolder = _outputPath is not null ? Path.GetDirectoryName(_outputPath)! : _sessionVm.SaveFolder;
        _confirmVm = new PreviewConfirmViewModel(crop, sourceHandle, _capture, _matcher, new CaptureSaver(saveFolder));
        if (_outputPath is not null)
        {
            _confirmVm.FileName = Path.GetFileName(_outputPath); // integrated: write exactly the requested file
        }
        if (fileName is not null)
        {
            _confirmVm.FileName = fileName;
        }
        if (confidence is not null)
        {
            _confirmVm.Confidence = confidence.Value;
        }

        var view = new PreviewConfirmView();
        view.Saved += OnConfirmSaved;
        view.RetakeRequested += (_, _) =>
        {
            DisposeConfirm();
            if (_regionVm is not null)
            {
                SetContent(BuildRegionView(_regionVm));
            }
            else
            {
                ReturnHome();
            }
        };
        view.Bind(_confirmVm);
        SetContent(view);
    }

    private void OnConfirmSaved(object? sender, string fileName)
    {
        if (_outputPath is not null)
        {
            Environment.ExitCode = 0;       // integrated single-shot: saved successfully
            Application.Current.Shutdown();
            return;
        }

        var path = Path.Combine(_sessionVm.SaveFolder, fileName);
        var confidence = _confirmVm!.Confidence;

        if (_editingRow is not null)
        {
            _editingRow.Confidence = confidence; // re-edit overwrote the file; update the row in place
            _editingRow = null;
        }
        else
        {
            _sessionVm.Add(path, confidence, _sourceHandle);
        }

        DisposeConfirm();
        DisposeRegion();
        ShowSession();
    }

    private void StartReEdit(SessionRow row)
    {
        var crop = LoadDetached(row.FilePath);
        if (crop is null)
        {
            return; // unreadable file; stay on the session panel
        }

        _editingRow = row;
        _sourceHandle = row.SourceHandle;
        DisposeRegion(); // no region step on re-edit
        ShowConfirm(crop, row.SourceHandle, row.FileName, row.Confidence);
    }

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose save folder",
            InitialDirectory = _sessionVm.SaveFolder,
        };
        if (dialog.ShowDialog() == true)
        {
            _sessionVm.SaveFolder = dialog.FolderName;
        }
    }

    // Load a PNG into an independent bitmap so the source file stays unlocked (re-edit Save can overwrite it).
    private static System.Drawing.Bitmap? LoadDetached(string path)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var loaded = new System.Drawing.Bitmap(stream);
            return new System.Drawing.Bitmap(loaded);
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultFolder()
    {
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
