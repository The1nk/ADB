using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Screen;
using AdbCore.Tests.Screen;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class TemplateMatchCoreTests
{
    [Fact]
    public void MatchInRegion_NoRegion_PassesFullHaystack_AndReturnsMatchUnchanged()
    {
        using var haystack = new Bitmap(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(50, 60, 10, 8, 0.95));

        var result = TemplateMatchCore.MatchInRegion(haystack, new Dictionary<string, object>(), matcher, "t.png", 0.8);

        Assert.Equal(1920, matcher.LastHaystackWidth);
        Assert.Equal(1080, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(50, 60, 10, 8, 0.95), result);
    }

    [Fact]
    public void MatchInRegion_WithRegion_CropsHaystack_AndOffsetsResultBack()
    {
        using var haystack = new Bitmap(1920, 1080);
        var matcher = new FakeTemplateMatcher(new MatchResult(5, 7, 10, 8, 0.9));
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        };

        var result = TemplateMatchCore.MatchInRegion(haystack, config, matcher, "t.png", 0.8);

        Assert.Equal(300, matcher.LastHaystackWidth);
        Assert.Equal(200, matcher.LastHaystackHeight);
        Assert.Equal(new MatchResult(105, 47, 10, 8, 0.9), result); // 5+100, 7+40
    }

    [Fact]
    public void MatchInRegion_RegionClampedToHaystack()
    {
        using var haystack = new Bitmap(200, 150);
        var matcher = new FakeTemplateMatcher(new MatchResult(0, 0, 1, 1, 0.9));
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 180, [TemplateMatchCore.RegionYKey] = 140,
            [TemplateMatchCore.RegionWidthKey] = 999, [TemplateMatchCore.RegionHeightKey] = 999,
        };

        TemplateMatchCore.MatchInRegion(haystack, config, matcher, "t.png", 0.8);

        Assert.Equal(20, matcher.LastHaystackWidth);  // 200-180
        Assert.Equal(10, matcher.LastHaystackHeight); // 150-140
    }

    [Fact]
    public void WriteMatchVariables_WritesEdgesCenterRandomAndScore()
    {
        var vars = new Dictionary<string, object>();
        var rng = new FixedRandomSource(123);

        TemplateMatchCore.WriteMatchVariables(vars, new MatchResult(100, 40, 30, 20, 0.97), "match", rng);

        Assert.Equal("100", vars["matchLeft"]);
        Assert.Equal("40", vars["matchTop"]);
        Assert.Equal("130", vars["matchRight"]);
        Assert.Equal("60", vars["matchBottom"]);
        Assert.Equal("115", vars["matchCenterX"]);
        Assert.Equal("50", vars["matchCenterY"]);
        Assert.Equal("123", vars["matchRandX"]);
        Assert.Equal("123", vars["matchRandY"]);
        Assert.Equal("0.97", vars["matchConfidence"]);

        // Random point must be requested strictly inside the match: X in [100,129], Y in [40,59].
        Assert.Equal((100, 129), rng.Calls[0]);
        Assert.Equal((40, 59), rng.Calls[1]);
    }

    [Fact]
    public void MatchInRegion_NoRegion_NoMatch_ReturnsNull()
    {
        using var haystack = new Bitmap(100, 100);
        var matcher = new FakeTemplateMatcher(null);

        Assert.Null(TemplateMatchCore.MatchInRegion(haystack, new Dictionary<string, object>(), matcher, "t.png", 0.8));
    }

    [Fact]
    public void MatchInRegion_WithRegion_NoMatch_ReturnsNull()
    {
        using var haystack = new Bitmap(100, 100);
        var matcher = new FakeTemplateMatcher(null);
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 10, [TemplateMatchCore.RegionYKey] = 10,
            [TemplateMatchCore.RegionWidthKey] = 50, [TemplateMatchCore.RegionHeightKey] = 50,
        };

        Assert.Null(TemplateMatchCore.MatchInRegion(haystack, config, matcher, "t.png", 0.8));
    }
}
