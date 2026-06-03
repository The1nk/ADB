using System.Drawing;
using System.IO;
using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class PreviewConfirmViewModelTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"botcap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static PreviewConfirmViewModel Make(
        string dir, FakeWindowCapture capture, FakeTemplateMatcher matcher, out Bitmap crop)
    {
        crop = new Bitmap(12, 8);
        return new PreviewConfirmViewModel(crop, (IntPtr)5, capture, matcher, new CaptureSaver(dir));
    }

    [Fact]
    public void FileName_SeededFromSaverNextName()
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                Assert.Equal("capture_001.png", vm.FileName);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.2, 0.0)]
    [InlineData(0.7, 0.7)]
    public void Confidence_ClampsToUnitRange(double set, double expected)
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                vm.Confidence = set;
                Assert.Equal(expected, vm.Confidence, 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_ScoreAtOrAboveConfidence_IsMatched()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Next = new MatchResult(3, 4, 12, 8, 0.95) };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.Confidence = 0.90;

                vm.TestMatch();

                Assert.NotNull(vm.LastOutcome);
                Assert.True(vm.LastOutcome!.Matched);
                Assert.Equal(0.95, vm.LastOutcome.Score!.Value, 3);
                Assert.Equal(-1.0, matcher.LastMinConfidence, 3); // asks for the best match regardless of threshold
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_BestScoreBelowConfidence_IsNotMatched_ButReportsScore()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 12, 8, 0.61) };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.Confidence = 0.90;

                vm.TestMatch();

                Assert.False(vm.LastOutcome!.Matched);
                Assert.Equal(0.61, vm.LastOutcome.Score!.Value, 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TestMatch_MatcherThrows_SetsErrorOutcome_NoException()
    {
        var dir = NewTempDir();
        try
        {
            var matcher = new FakeTemplateMatcher { Throw = new InvalidOperationException("bad template") };
            var vm = Make(dir, new FakeWindowCapture(), matcher, out var crop);
            using (crop)
            {
                vm.TestMatch();

                Assert.NotNull(vm.LastOutcome);
                Assert.False(vm.LastOutcome!.Matched);
                Assert.False(string.IsNullOrEmpty(vm.LastOutcome.Error));
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Save_WritesPngAndSidecarAtChosenNameAndConfidence()
    {
        var dir = NewTempDir();
        try
        {
            var vm = Make(dir, new FakeWindowCapture(), new FakeTemplateMatcher(), out var crop);
            using (crop)
            {
                vm.FileName = "btn.png";
                vm.Confidence = 0.77;

                vm.Save();

                var png = Path.Combine(dir, "btn.png");
                Assert.True(File.Exists(png));
                Assert.Equal(0.77, ConfidenceSidecar.Read(png, 0.0), 3);
            }
        }
        finally { Directory.Delete(dir, true); }
    }
}
