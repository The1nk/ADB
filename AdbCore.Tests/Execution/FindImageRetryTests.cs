using System.Drawing;
using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using AdbCore.Screen;
using Xunit;

namespace AdbCore.Tests.Execution;

public class FindImageRetryTests
{
    private sealed class FlakyMatcher(int failuresBeforeHit, MatchResult hit) : ITemplateMatcher
    {
        private int _calls;
        public MatchResult? Match(Bitmap haystack, string templatePath, double minConfidence)
            => ++_calls > failuresBeforeHit ? hit : null;
    }

    private sealed class StubCapture : IWindowCapture
    {
        public Bitmap Capture(IntPtr windowHandle, ScreenCaptureMethod method) => new(64, 64);
    }

    [Fact]
    public async Task FindImage_WithRetry_KeepsTryingUntilFound()
    {
        var targetId = Guid.NewGuid();
        var execs = new ActionExecutorRegistry();
        execs.Register(new StartAction());
        execs.Register(new EndAction());
        execs.Register(new FindImageAction(new StubCapture(), new FlakyMatcher(2, new MatchResult(10, 10, 4, 4, 0.99)), new SystemRandomSource()));

        var start = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.start" };
        var find = new BotAction
        {
            Id = Guid.NewGuid(), TypeKey = "screen.findImage", TargetId = targetId,
            Config = { [FindImageAction.TemplatePathKey] = "x.png" },
            Retry = new RetryPolicy { MaxAttempts = 3, DelayMs = 0 },
        };
        var end = new BotAction { Id = Guid.NewGuid(), TypeKey = "control.end" };

        var bot = new Bot { Actions = { start, find, end }, Connections =
        {
            new ActionConnection { SourceActionId = start.Id, SourcePort = "out", TargetActionId = find.Id, TargetPort = "in" },
            new ActionConnection { SourceActionId = find.Id, SourcePort = "onSuccess", TargetActionId = end.Id, TargetPort = "in" },
        } };

        var options = new ExecutionOptions
        {
            ResolvedTargets = new Dictionary<Guid, ResolvedTarget>
            {
                [targetId] = new ResolvedTarget { Type = BotTargetType.Window, Selector = "hwnd:1", Handle = (IntPtr)5 },
            },
        };

        var result = await new BotExecutor(execs).RunAsync(bot, options, null, default);

        // Reaching End (Success) is only possible because the action retried past the 2 misses to the hit.
        Assert.True(result.Success);
    }
}
