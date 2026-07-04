using System.Drawing;
using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SessionViewModelTests
{
    private static SessionViewModel Make(FakeTemplateMatcher matcher) =>
        new(matcher, saveFolder: @"C:\bots");

    // Retest reads the saved template from disk as bytes (the matcher is embedded-bytes-only), so tests
    // that exercise the actual match/no-match path need a real PNG file on disk, not a bogus path.
    private static string CreateTempPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"botcap_sessionvm_test_{Guid.NewGuid():N}.png");
        using var bmp = new Bitmap(4, 4);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Add_AppendsRowWithDetails()
    {
        var vm = Make(new FakeTemplateMatcher());
        var source = new FakeCaptureSource();

        var row = vm.Add(@"C:\bots\a.png", 0.88, source);

        Assert.Single(vm.Rows);
        Assert.Same(row, vm.Rows[0]);
        Assert.Equal(@"C:\bots\a.png", row.FilePath);
        Assert.Equal("a.png", row.FileName);
        Assert.Equal(0.88, row.Confidence, 3);
        Assert.Same(source, row.Source);
        Assert.Null(row.LastRetestMatched);
    }

    [Fact]
    public void Remove_DropsRow()
    {
        var vm = Make(new FakeTemplateMatcher());
        var row = vm.Add(@"C:\bots\a.png", 0.9, new FakeCaptureSource());

        vm.Remove(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Retest_Match_SetsGreen_RecapturesSource_UsesRowConfidenceAndTemplate()
    {
        var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 4, 4, 0.97) };
        var vm = Make(matcher);
        var source = new FakeCaptureSource();
        var path = CreateTempPng();

        try
        {
            var row = vm.Add(path, 0.80, source);

            vm.Retest(row);

            Assert.True(row.LastRetestMatched);
            Assert.Equal(1, source.CaptureCalls);
            Assert.Equal(0.80, matcher.LastMinConfidence, 3);
            Assert.NotNull(matcher.LastTemplateBytes); // read from disk as bytes — matcher is embedded-bytes-only
            Assert.NotEmpty(matcher.LastTemplateBytes!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Retest_NoMatch_SetsRed()
    {
        var vm = Make(new FakeTemplateMatcher { Next = null });
        var path = CreateTempPng();

        try
        {
            var row = vm.Add(path, 0.95, new FakeCaptureSource());

            vm.Retest(row);

            Assert.False(row.LastRetestMatched);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Retest_CaptureThrows_SetsRed_NoException()
    {
        var vm = Make(new FakeTemplateMatcher());
        var source = new FakeCaptureSource { Behavior = () => throw new InvalidOperationException("device gone") };
        var row = vm.Add(@"C:\bots\a.png", 0.9, source);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }
}
