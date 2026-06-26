using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SessionViewModelTests
{
    private static SessionViewModel Make(FakeTemplateMatcher matcher) =>
        new(matcher, saveFolder: @"C:\bots");

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
        var row = vm.Add(@"C:\bots\a.png", 0.80, source);

        vm.Retest(row);

        Assert.True(row.LastRetestMatched);
        Assert.Equal(1, source.CaptureCalls);
        Assert.Equal(0.80, matcher.LastMinConfidence, 3);
        Assert.Equal(@"C:\bots\a.png", matcher.LastTemplatePath);
    }

    [Fact]
    public void Retest_NoMatch_SetsRed()
    {
        var vm = Make(new FakeTemplateMatcher { Next = null });
        var row = vm.Add(@"C:\bots\a.png", 0.95, new FakeCaptureSource());

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
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
