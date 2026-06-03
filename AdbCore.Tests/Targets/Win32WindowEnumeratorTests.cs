using AdbCore.Targets;

namespace AdbCore.Tests.Targets;

public class Win32WindowEnumeratorTests
{
    [Theory]
    [InlineData(true, 5, true)]    // visible + titled -> include
    [InlineData(false, 5, false)]  // hidden -> exclude
    [InlineData(true, 0, false)]   // untitled -> exclude
    [InlineData(false, 0, false)]  // hidden + untitled -> exclude
    public void ShouldInclude_RequiresVisibleAndNonEmptyTitle(bool isVisible, int titleLength, bool expected)
    {
        Assert.Equal(expected, Win32WindowEnumerator.ShouldInclude(isVisible, titleLength));
    }
}
