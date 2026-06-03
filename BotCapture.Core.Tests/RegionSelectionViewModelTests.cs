using System.Drawing;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class RegionSelectionViewModelTests
{
    [Theory]
    // in-bounds rect passes through
    [InlineData(10, 10, 20, 15, 10, 10, 20, 15)]
    // negative width/height (drag up-left) normalizes
    [InlineData(30, 25, -20, -15, 10, 10, 20, 15)]
    // overflow clamps to image bounds (image is 100x80)
    [InlineData(90, 70, 50, 50, 90, 70, 10, 10)]
    public void ClampSelection_NormalizesAndClamps(
        int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        var clamped = RegionSelectionViewModel.ClampSelection(new Rectangle(x, y, w, h), 100, 80);
        Assert.Equal(new Rectangle(ex, ey, ew, eh), clamped);
    }

    [Fact]
    public void ClampSelection_ZeroSize_BecomesOnePixel()
    {
        var clamped = RegionSelectionViewModel.ClampSelection(new Rectangle(5, 5, 0, 0), 100, 80);
        Assert.Equal(1, clamped.Width);
        Assert.Equal(1, clamped.Height);
    }

    [Fact]
    public void Crop_ReturnsBitmapOfClampedSelectionSize()
    {
        using var source = new Bitmap(100, 80);
        var vm = new RegionSelectionViewModel(source);
        vm.Selection = new Rectangle(10, 10, 20, 15);

        using var crop = vm.Crop();

        Assert.Equal(20, crop.Width);
        Assert.Equal(15, crop.Height);
    }
}
