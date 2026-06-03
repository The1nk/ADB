using AdbCore.Screen;
using AdbCore.Targets;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class WindowPickerViewModelTests
{
    private static WindowPickerViewModel Make(out FakeWindowEnumerator enumerator, out FakeWindowCapture capture)
    {
        enumerator = new FakeWindowEnumerator();
        capture = new FakeWindowCapture();
        return new WindowPickerViewModel(enumerator, capture);
    }

    [Fact]
    public void Refresh_MapsEnumeratedWindowsToRowsInOrder()
    {
        var vm = Make(out var enumerator, out _);
        enumerator.Result = new[]
        {
            new WindowInfo((IntPtr)1, "Alpha", "alpha"),
            new WindowInfo((IntPtr)2, "Beta", "beta"),
        };

        vm.Refresh();

        Assert.Equal(2, vm.Windows.Count);
        Assert.Equal("Alpha", vm.Windows[0].Title);
        Assert.Equal("Beta", vm.Windows[1].Title);
        Assert.NotNull(vm.Windows[0].ThumbnailPng);
    }

    [Fact]
    public void Refresh_NoWindows_ProducesNoRows()
    {
        var vm = Make(out _, out _);

        vm.Refresh();

        Assert.Empty(vm.Windows);
    }

    [Fact]
    public void Refresh_ThumbnailCaptureThrows_RowStillAddedWithNullThumbnail()
    {
        var vm = Make(out var enumerator, out var capture);
        enumerator.Result = new[] { new WindowInfo((IntPtr)1, "Alpha", "alpha") };
        capture.Behavior = _ => throw new InvalidOperationException("unrenderable");

        vm.Refresh();

        Assert.Single(vm.Windows);
        Assert.Null(vm.Windows[0].ThumbnailPng);
    }

    [Fact]
    public void CaptureSelected_UsesSelectedHandleAndPrintWindow_SetsCapturedImage()
    {
        var vm = Make(out _, out var capture);
        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)42, "Game", "game"), null);

        var ok = vm.CaptureSelected();

        Assert.True(ok);
        Assert.NotNull(vm.CapturedImage);
        var last = capture.Calls[^1];
        Assert.Equal((IntPtr)42, last.Handle);
        Assert.Equal(ScreenCaptureMethod.Auto, last.Method);
    }

    [Fact]
    public void CaptureSelected_NoSelection_ReturnsFalseAndSetsStatus()
    {
        var vm = Make(out _, out _);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void CaptureSelected_CaptureThrows_ReturnsFalseAndSetsStatus_NoException()
    {
        var vm = Make(out _, out var capture);
        capture.Behavior = _ => throw new InvalidOperationException("boom");
        vm.SelectedWindow = new WindowRow(new WindowInfo((IntPtr)7, "X", "x"), null);

        var ok = vm.CaptureSelected();

        Assert.False(ok);
        Assert.Null(vm.CapturedImage);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }
}
