using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Ocr;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Ocr;

public class OcrCoreTests
{
    private static OcrResult Result(params OcrWord[] words) => new(string.Join(" ", System.Array.ConvertAll(words, w => w.Text)), words);

    [Fact]
    public void RecognizeRegion_NoRegion_PassesFullImage()
    {
        using var img = new Bitmap(800, 600);
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(1, 2, 3, 4), 0.9)));

        var res = OcrCore.RecognizeRegion(img, new Dictionary<string, object>(), engine);

        Assert.Equal(800, engine.LastWidth);
        Assert.Equal(600, engine.LastHeight);
        Assert.Equal("hi", res.Words[0].Text);
    }

    [Fact]
    public void RecognizeRegion_WithRegion_CropsAndOffsetsWordBoxesBack()
    {
        using var img = new Bitmap(800, 600);
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(5, 7, 10, 8), 0.9))); // crop-local
        var config = new Dictionary<string, object>
        {
            [TemplateMatchCore.RegionXKey] = 100, [TemplateMatchCore.RegionYKey] = 40,
            [TemplateMatchCore.RegionWidthKey] = 300, [TemplateMatchCore.RegionHeightKey] = 200,
        };

        var res = OcrCore.RecognizeRegion(img, config, engine);

        Assert.Equal(300, engine.LastWidth);
        Assert.Equal(200, engine.LastHeight);
        Assert.Equal(new Rectangle(105, 47, 10, 8), res.Words[0].Bounds); // offset by ROI origin
    }

    [Fact]
    public void FindWord_CaseInsensitiveSubstring_ReturnsFirstMatchBox()
    {
        var res = Result(
            new OcrWord("Settings", new Rectangle(0, 0, 80, 18), 0.95),
            new OcrWord("ATTACK", new Rectangle(120, 40, 70, 20), 0.88));

        var m = OcrCore.FindWord(res, "attack", 0.0);

        Assert.NotNull(m);
        Assert.Equal(new MatchResult(120, 40, 70, 20, 0.88), m);
    }

    [Fact]
    public void FindWord_BelowMinConfidence_NotMatched()
    {
        var res = Result(new OcrWord("attack", new Rectangle(1, 2, 3, 4), 0.40));
        Assert.Null(OcrCore.FindWord(res, "attack", 0.80));
    }

    [Fact]
    public void FindWord_NoMatch_ReturnsNull()
    {
        var res = Result(new OcrWord("hello", new Rectangle(1, 2, 3, 4), 0.9));
        Assert.Null(OcrCore.FindWord(res, "attack", 0.0));
    }
}
