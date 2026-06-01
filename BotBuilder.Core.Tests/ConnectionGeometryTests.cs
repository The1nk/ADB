using BotBuilder.Core;
using BotBuilder.Core.Connections;
using Xunit;

namespace BotBuilder.Core.Tests;

public class ConnectionGeometryTests
{
    [Fact]
    public void BuildPath_ProducesInvariantCultureBezierString()
    {
        var path = ConnectionGeometry.BuildPath(new CanvasPoint(0, 0), new CanvasPoint(100, 50));

        Assert.StartsWith("M 0,0 C ", path);
        Assert.Contains(",", path);
        Assert.DoesNotContain(";", path);
        Assert.Equal(path, path.Trim());
    }

    [Fact]
    public void ControlPoints_ExtendHorizontallyByAtLeastMinimum()
    {
        var (c1, c2) = ConnectionGeometry.ControlPoints(new CanvasPoint(0, 0), new CanvasPoint(10, 0));

        Assert.True(c1.X >= 40);
        Assert.Equal(0, c1.Y);
        Assert.Equal(10 - (c1.X - 0), c2.X, 3);
    }
}
