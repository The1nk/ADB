using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class RunnerLogParserTests
{
    [Fact]
    public void Parse_RunStart()
    {
        var e = RunnerLogParser.Parse("""{"event":"run-start","bot":"Farm"}""");
        Assert.Equal(RunLogKind.RunStart, e.Kind);
        Assert.Equal("▶ run started", e.Display);
    }

    [Fact]
    public void Parse_ActionSuccess_ShowsLabel()
    {
        var e = RunnerLogParser.Parse("""{"event":"action","actionId":"a1","label":"Find Attack","success":true}""");
        Assert.Equal(RunLogKind.Action, e.Kind);
        Assert.Equal("a1", e.ActionId);
        Assert.True(e.Success);
        Assert.Equal("✓ Find Attack", e.Display);
    }

    [Fact]
    public void Parse_ActionFailure_ShowsError()
    {
        var e = RunnerLogParser.Parse("""{"event":"action","actionId":"a2","label":"Tap","success":false,"error":"no match"}""");
        Assert.Equal(RunLogKind.Action, e.Kind);
        Assert.False(e.Success);
        Assert.Equal("✗ Tap: no match", e.Display);
    }

    [Fact]
    public void Parse_RunEndFailure()
    {
        var e = RunnerLogParser.Parse("""{"event":"run-end","success":false,"error":"halted"}""");
        Assert.Equal(RunLogKind.RunEnd, e.Kind);
        Assert.Equal("■ run failed: halted", e.Display);
    }

    [Fact]
    public void Parse_LogMessage()
    {
        var e = RunnerLogParser.Parse("""{"event":"log","message":"hello"}""");
        Assert.Equal(RunLogKind.Message, e.Kind);
        Assert.Equal("hello", e.Display);
    }

    [Fact]
    public void Parse_RunEndSuccess()
    {
        var e = RunnerLogParser.Parse("""{"event":"run-end","success":true,"actionsExecuted":5}""");
        Assert.Equal(RunLogKind.RunEnd, e.Kind);
        Assert.Equal("■ run succeeded", e.Display);
    }

    [Fact]
    public void Parse_Action_NoLabel_FallsBackToActionId()
    {
        var e = RunnerLogParser.Parse("""{"event":"action","actionId":"a3","success":true}""");
        Assert.Equal(RunLogKind.Action, e.Kind);
        Assert.Equal("✓ a3", e.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ broken json")]
    public void Parse_Garbage_IsUnparsed_NotThrown(string line)
    {
        var e = RunnerLogParser.Parse(line);
        Assert.Equal(RunLogKind.Unparsed, e.Kind);
        Assert.Equal(line, e.Display); // raw passthrough
    }
}
