using AdbCore.Screen;
using BotCapture.Core;

namespace BotCapture.Core.Tests;

public class SessionViewModelTests
{
    private static SessionViewModel Make(FakeWindowCapture capture, FakeTemplateMatcher matcher) =>
        new(capture, matcher, saveFolder: @"C:\bots");

    [Fact]
    public void Add_AppendsRowWithDetails()
    {
        var vm = Make(new FakeWindowCapture(), new FakeTemplateMatcher());

        var row = vm.Add(@"C:\bots\a.png", 0.88, (IntPtr)7);

        Assert.Single(vm.Rows);
        Assert.Same(row, vm.Rows[0]);
        Assert.Equal(@"C:\bots\a.png", row.FilePath);
        Assert.Equal("a.png", row.FileName);
        Assert.Equal(0.88, row.Confidence, 3);
        Assert.Equal((IntPtr)7, row.SourceHandle);
        Assert.Null(row.LastRetestMatched);
    }

    [Fact]
    public void Remove_DropsRow()
    {
        var vm = Make(new FakeWindowCapture(), new FakeTemplateMatcher());
        var row = vm.Add(@"C:\bots\a.png", 0.9, (IntPtr)1);

        vm.Remove(row);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Retest_Match_SetsGreen_UsesRowHandleAndConfidence()
    {
        var capture = new FakeWindowCapture();
        var matcher = new FakeTemplateMatcher { Next = new MatchResult(0, 0, 4, 4, 0.97) };
        var vm = Make(capture, matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.80, (IntPtr)42);

        vm.Retest(row);

        Assert.True(row.LastRetestMatched);
        Assert.Equal((IntPtr)42, capture.Calls[^1].Handle);
        Assert.Equal(0.80, matcher.LastMinConfidence, 3);
        Assert.Equal(@"C:\bots\a.png", matcher.LastTemplatePath);
    }

    [Fact]
    public void Retest_NoMatch_SetsRed()
    {
        var matcher = new FakeTemplateMatcher { Next = null };
        var vm = Make(new FakeWindowCapture(), matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.95, (IntPtr)1);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }

    [Fact]
    public void Retest_MatcherThrows_SetsRed_NoException()
    {
        var matcher = new FakeTemplateMatcher { Throw = new FileNotFoundException("gone") };
        var vm = Make(new FakeWindowCapture(), matcher);
        var row = vm.Add(@"C:\bots\a.png", 0.9, (IntPtr)1);

        vm.Retest(row);

        Assert.False(row.LastRetestMatched);
    }
}
