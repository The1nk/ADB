using System.Collections.Generic;
using System.Drawing;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Android;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Ocr;
using AdbCore.Tests.Ocr;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn.Android;

public class AndroidOcrActionBaseTests
{
    private sealed class TestAndroidOcrAction(IOcrEngine ocr) : AndroidOcrActionBase(ocr)
    {
        public override string TypeKey => "android.testOcr";
        public override string DisplayName => "Test Android OCR";
        public override string Description => "";
        protected override IEnumerable<ConfigField> ActionConfigFields => [];
        public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct) => Task.FromResult(ActionResult.Ok(SuccessPort));

        public OcrResult CallRecognizeDevice(ActionExecutionContext ctx, IAndroidDevice device) => RecognizeDevice(ctx, device);
    }

    private static OcrResult Result(params OcrWord[] w) => new(string.Join(" ", System.Array.ConvertAll(w, x => x.Text)), w);
    private static ActionExecutionContext Exec(BotAction a) => new(a, new BotExecutionContext(), _ => { });

    [Fact]
    public void RecognizeDevice_DecodesFramebuffer_AndRunsOcr()
    {
        var device = new FakeAndroidDevice { ScreenshotBytes = AndroidImageActionBaseTests.PngBytes(1080, 1920) };
        var engine = new FakeOcrEngine(Result(new OcrWord("hi", new Rectangle(1, 2, 3, 4), 0.9)));
        var action = new TestAndroidOcrAction(engine);

        var res = action.CallRecognizeDevice(Exec(new BotAction()), device);

        Assert.Equal(1080, engine.LastWidth);
        Assert.Equal(1920, engine.LastHeight);
        Assert.Equal("hi", res.Words[0].Text);
        Assert.Contains("screenshot", device.Calls);
    }

    [Fact]
    public void Definition_HasRegionFields_SupportsRetry_CategoryAndroid()
    {
        var action = new TestAndroidOcrAction(new FakeOcrEngine(Result()));
        Assert.Equal("Android", action.Category);
        Assert.True(action.SupportsRetry);
        Assert.Contains(action.ConfigFields, f => f.Key == TemplateMatchCore.RegionWidthKey);
    }
}
