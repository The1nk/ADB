using AdbCore.Scripting;
using MoonSharp.Interpreter;
using Xunit;

namespace AdbCore.Tests.Scripting;

public class LuaValuesTests
{
    [Fact]
    public void ToDynValue_MapsClrTypes()
    {
        Assert.Equal(DataType.String, LuaValues.ToDynValue("hi").Type);
        Assert.Equal(DataType.Number, LuaValues.ToDynValue(3.5).Type);
        Assert.Equal(DataType.Boolean, LuaValues.ToDynValue(true).Type);
        Assert.Equal("hi", LuaValues.ToDynValue("hi").String);
        Assert.Equal(3.5, LuaValues.ToDynValue(3.5).Number);
        Assert.True(LuaValues.ToDynValue(true).Boolean);
        Assert.False(LuaValues.ToDynValue(false).Boolean);
    }

    [Fact]
    public void ToDynValue_IntAndNull()
    {
        Assert.Equal(7d, LuaValues.ToDynValue(7).Number);       // int -> Lua number
        Assert.Equal(2.5d, LuaValues.ToDynValue(2.5f).Number);  // float -> Lua number
        Assert.Equal(9d, LuaValues.ToDynValue(9L).Number);      // long -> Lua number
        Assert.Equal(DataType.Nil, LuaValues.ToDynValue(null).Type);

        var jsonNull = System.Text.Json.JsonDocument.Parse("null").RootElement;
        Assert.Equal(DataType.Nil, LuaValues.ToDynValue(jsonNull).Type);
    }

    [Fact]
    public void ToClr_MapsBackToStringNumberBool()
    {
        Assert.Equal("hi", LuaValues.ToClr(DynValue.NewString("hi")));
        Assert.Equal(3.5, LuaValues.ToClr(DynValue.NewNumber(3.5)));
        Assert.Equal(true, LuaValues.ToClr(DynValue.NewBoolean(true)));
        Assert.Null(LuaValues.ToClr(DynValue.Nil));
    }
}
