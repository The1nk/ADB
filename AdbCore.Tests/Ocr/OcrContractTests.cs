using System.Collections.Generic;
using System.Drawing;
using AdbCore.Ocr;
using Xunit;

namespace AdbCore.Tests.Ocr;

public class OcrContractTests
{
    [Fact]
    public void OcrResult_CarriesTextAndWords()
    {
        var word = new OcrWord("Attack", new Rectangle(10, 20, 50, 18), 0.91);
        var result = new OcrResult("Attack Now", new List<OcrWord> { word });

        Assert.Equal("Attack Now", result.Text);
        Assert.Equal("Attack", result.Words[0].Text);
        Assert.Equal(new Rectangle(10, 20, 50, 18), result.Words[0].Bounds);
        Assert.Equal(0.91, result.Words[0].Confidence);
    }
}
