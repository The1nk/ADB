using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Screen;

public class SystemRandomSourceTests
{
    [Fact]
    public void Next_StaysWithinInclusiveBounds()
    {
        var rng = new SystemRandomSource();
        for (var i = 0; i < 1000; i++)
        {
            var v = rng.Next(10, 12);
            Assert.InRange(v, 10, 12);
        }
    }

    [Fact]
    public void Next_MinEqualsMax_ReturnsThatValue()
        => Assert.Equal(7, new SystemRandomSource().Next(7, 7));
}
