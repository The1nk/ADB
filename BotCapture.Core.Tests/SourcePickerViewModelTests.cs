using AdbCore.Android;
using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SourcePickerViewModelTests
{
    private static SourcePickerViewModel Make(
        out FakeWindowEnumerator windows, out FakeWindowCapture capture,
        out FakeAdbDevices devices, out FakeAndroidDeviceConnector connector)
    {
        windows = new FakeWindowEnumerator();
        capture = new FakeWindowCapture();
        devices = new FakeAdbDevices();
        connector = new FakeAndroidDeviceConnector();
        return new SourcePickerViewModel(windows, capture, devices, connector);
    }

    [Fact]
    public void Refresh_Windows_MapsEnumeratedWindowsToRowsInOrder()
    {
        var vm = Make(out var windows, out _, out _, out _);
        windows.Result = new[]
        {
            new WindowInfo((IntPtr)1, "Alpha", "alpha"),
            new WindowInfo((IntPtr)2, "Beta", "beta"),
        };

        vm.Refresh();

        Assert.Equal(2, vm.Sources.Count);
        Assert.Equal("Alpha", vm.Sources[0].Label);
        Assert.Equal("beta", vm.Sources[1].SubLabel);
        Assert.NotNull(vm.Sources[0].ThumbnailPng);
    }

    [Fact]
    public void SwitchingKindToAndroid_RebuildsListFromDevices()
    {
        var vm = Make(out var windows, out _, out var devices, out _);
        windows.Result = new[] { new WindowInfo((IntPtr)1, "Alpha", "alpha") };
        devices.Result = new[] { new AdbDeviceInfo("emulator-5554", "device") };
        vm.Refresh(); // windows kind (default)
        Assert.Single(vm.Sources);

        vm.Kind = SourceKind.Android;

        Assert.Single(vm.Sources);
        Assert.Equal("emulator-5554", vm.Sources[0].Label);
        Assert.Equal("device", vm.Sources[0].SubLabel);
        Assert.Null(vm.UnavailableReason);
    }

    [Fact]
    public void Android_AdbMissing_SetsUnavailableReason_NoRows()
    {
        var vm = Make(out _, out _, out var devices, out _);
        devices.Throw = new InvalidOperationException("adb server failed to start");

        vm.Kind = SourceKind.Android;

        Assert.Empty(vm.Sources);
        Assert.False(string.IsNullOrEmpty(vm.UnavailableReason));
        Assert.Contains("adb", vm.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Android_NoDevices_SetsUnavailableReason_NoRows()
    {
        var vm = Make(out _, out _, out var devices, out _);
        devices.Result = Array.Empty<AdbDeviceInfo>();

        vm.Kind = SourceKind.Android;

        Assert.Empty(vm.Sources);
        Assert.Contains("No devices", vm.UnavailableReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaptureSelected_UsesSelectedSource_SetsCapturedImage()
    {
        var vm = Make(out _, out _, out _, out _);
        var source = new FakeCaptureSource();
        vm.SelectedSource = new CaptureSourceRow(source, "X", "x", null);

        var ok = vm.CaptureSelected();

        Assert.True(ok);
        Assert.NotNull(vm.CapturedImage);
        Assert.Equal(1, source.CaptureCalls);
        Assert.Same(source, vm.SelectedCaptureSource);
    }

    [Fact]
    public void CaptureSelected_NoSelection_ReturnsFalseAndSetsStatus()
    {
        var vm = Make(out _, out _, out _, out _);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void CaptureSelected_CaptureThrows_ReturnsFalseAndSetsStatus_NoException()
    {
        var vm = Make(out _, out _, out _, out _);
        vm.SelectedSource = new CaptureSourceRow(
            new FakeCaptureSource { Behavior = () => throw new InvalidOperationException("boom") }, "X", "x", null);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void TakeCapturedImage_TransfersOwnership_ClearsWithoutDisposing()
    {
        var vm = Make(out _, out _, out _, out _);
        vm.SelectedSource = new CaptureSourceRow(new FakeCaptureSource(), "A", "a", null);
        vm.CaptureSelected();

        var taken = vm.TakeCapturedImage();

        Assert.NotNull(taken);
        Assert.Null(vm.CapturedImage);
        Assert.False(vm.HasCapture);
        Assert.Equal(8, taken!.Width); // FakeCaptureSource returns an 8x8 bitmap; still usable (not disposed)
        taken.Dispose();
    }
}
