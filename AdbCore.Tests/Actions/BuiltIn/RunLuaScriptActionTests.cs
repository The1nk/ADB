using AdbCore.Actions.BuiltIn;
using AdbCore.Execution;
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class RunLuaScriptActionTests
{
    private static ActionExecutionContext Exec(BotAction a, BotExecutionContext c, System.Action<string> log) => new(a, c, log);

    [Fact]
    public async Task Script_SetsVariable_AndSucceeds()
    {
        var ctx = new BotExecutionContext { Variables = { ["i"] = 1d } };
        var action = new BotAction { Config = { [RunLuaScriptAction.ScriptKey] = "vars.i = vars.i + 1" } };

        var r = await new RunLuaScriptAction().ExecuteAsync(Exec(action, ctx, _ => { }), default);

        Assert.True(r.Success);
        Assert.Equal("onSuccess", r.OutputPort);
        Assert.Equal(2d, ctx.Variables["i"]);
    }

    [Fact]
    public async Task LuaError_RoutesOnFailure()
    {
        var action = new BotAction { Config = { [RunLuaScriptAction.ScriptKey] = "error('nope')" } };
        var r = await new RunLuaScriptAction().ExecuteAsync(Exec(action, new BotExecutionContext(), _ => { }), default);
        Assert.False(r.Success);
        Assert.Contains("nope", r.ErrorMessage);
    }

    [Fact]
    public async Task Log_EmitsToSink()
    {
        var logged = new System.Collections.Generic.List<string>();
        var action = new BotAction { Config = { [RunLuaScriptAction.ScriptKey] = "log('hi')" } };
        await new RunLuaScriptAction().ExecuteAsync(Exec(action, new BotExecutionContext(), logged.Add), default);
        Assert.Contains("hi", logged);
    }

    [Fact]
    public void Definition_Metadata()
    {
        var def = new RunLuaScriptAction();
        Assert.Equal("scripting.runLua", def.TypeKey);
        Assert.Equal("Run Lua Script", def.DisplayName);
        Assert.Equal("Scripting", def.Category);
        Assert.Equal(new[] { "onSuccess", "onFailure" }, def.OutputPorts.Select(p => p.Name));
        Assert.Contains(def.ConfigFields, f => f.Key == RunLuaScriptAction.ScriptKey && f.Type == AdbCore.Actions.ConfigFieldType.MultilineString);
    }
}
