using System.Collections.Generic;
using System.Linq;
using AdbCore.Actions;
using AdbCore.Actions.BuiltIn;
using Xunit;

namespace AdbCore.Tests.Actions.BuiltIn;

public class FrameSourceConfigTests
{
    [Fact]
    public void UsesStoredFrame_DefaultsToFalse_WhenUnset()
    {
        var config = new Dictionary<string, object>();
        Assert.False(FrameSourceConfig.UsesStoredFrame(config));
    }

    [Fact]
    public void UsesStoredFrame_TrueForStored_CaseInsensitive()
    {
        var config = new Dictionary<string, object> { [FrameSourceConfig.SourceKey] = "stored" };
        Assert.True(FrameSourceConfig.UsesStoredFrame(config));
    }

    [Fact]
    public void FrameNameOf_DefaultsToFrame_WhenUnsetOrBlank()
    {
        Assert.Equal("frame", FrameSourceConfig.FrameNameOf(new Dictionary<string, object>()));
        Assert.Equal("frame", FrameSourceConfig.FrameNameOf(new Dictionary<string, object> { [FrameSourceConfig.FrameNameKey] = "  " }));
    }

    [Fact]
    public void FrameNameOf_ReturnsConfiguredName()
    {
        var config = new Dictionary<string, object> { [FrameSourceConfig.FrameNameKey] = "hp" };
        Assert.Equal("hp", FrameSourceConfig.FrameNameOf(config));
    }

    [Fact]
    public void Fields_AreSourceThenFrameName()
    {
        var keys = FrameSourceConfig.Fields().Select(f => f.Key).ToArray();
        Assert.Equal(new[] { FrameSourceConfig.SourceKey, FrameSourceConfig.FrameNameKey }, keys);
        Assert.Equal(ConfigFieldType.Enum, FrameSourceConfig.Fields().First().Type);
    }
}
