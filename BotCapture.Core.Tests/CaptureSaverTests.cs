using System.Drawing;
using System.IO;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class CaptureSaverTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void NextFileName_EmptyFolder_IsCaptureOne()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Equal("capture_001.png", new CaptureSaver(dir).NextFileName());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void NextFileName_SkipsExisting_PicksLowestFree()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "capture_001.png"), "x");
            File.WriteAllText(Path.Combine(dir, "capture_003.png"), "x");
            Assert.Equal("capture_002.png", new CaptureSaver(dir).NextFileName());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_WritesPngAndSidecar()
    {
        var dir = NewTempDir();
        try
        {
            var saver = new CaptureSaver(dir);
            using var crop = new Bitmap(10, 6);

            saver.Save(crop, "attack-btn.png", 0.88);

            var png = Path.Combine(dir, "attack-btn.png");
            Assert.True(File.Exists(png));
            Assert.True(File.Exists(png + ".meta.json"));
            Assert.Equal(0.88, ConfidenceSidecar.Read(png, fallback: 0.0), 3);
            using var reread = new Bitmap(png);
            Assert.Equal(10, reread.Width);
        }
        finally { Directory.Delete(dir, true); }
    }
}
