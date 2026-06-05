using BotBuilder.Core.Palette;
using Xunit;

namespace BotBuilder.Core.Tests;

public class DependencyProbeTests
{
    [Fact]
    public void Android_available_when_check_passes()
    {
        var probe = new DependencyProbe(androidAvailable: () => true, browserAvailable: () => false);

        var status = probe.ForCategory("Android");

        Assert.True(status.IsAvailable);
        Assert.Null(status.Reason);
    }

    [Fact]
    public void Android_unavailable_reports_path_reason()
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => true);

        var status = probe.ForCategory("Android");

        Assert.False(status.IsAvailable);
        Assert.Equal("adb not found on PATH", status.Reason);
    }

    [Fact]
    public void Browser_available_when_check_passes()
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => true);

        Assert.True(probe.ForCategory("Browser").IsAvailable);
    }

    [Fact]
    public void Browser_unavailable_reports_install_reason()
    {
        var probe = new DependencyProbe(androidAvailable: () => true, browserAvailable: () => false);

        var status = probe.ForCategory("Browser");

        Assert.False(status.IsAvailable);
        Assert.Equal("No browser engine found — run 'playwright install'", status.Reason);
    }

    [Theory]
    [InlineData("Screen")]
    [InlineData("Input")]
    [InlineData("Scripting")]
    [InlineData("Control Flow")]
    public void Other_categories_are_always_available(string category)
    {
        var probe = new DependencyProbe(androidAvailable: () => false, browserAvailable: () => false);

        var status = probe.ForCategory(category);

        Assert.True(status.IsAvailable);
        Assert.Null(status.Reason);
    }
}
