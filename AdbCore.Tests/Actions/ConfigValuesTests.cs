using System.Text.Json;
using AdbCore.Actions;
using Xunit;

namespace AdbCore.Tests.Actions;

public class ConfigValuesTests
{
    private static Dictionary<string, object> Config(string key, object value)
        => new() { [key] = value };

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void GetString_BoxedString_Reads()
        => Assert.Equal("hi", ConfigValues.GetString(Config("k", "hi"), "k"));

    [Fact]
    public void GetString_FromJsonElement_Reads()
        => Assert.Equal("hi", ConfigValues.GetString(Config("k", Json("\"hi\"")), "k"));

    [Fact]
    public void GetString_Missing_ReturnsFallback()
        => Assert.Equal("fb", ConfigValues.GetString(new Dictionary<string, object>(), "k", "fb"));

    [Fact]
    public void GetInt_BoxedDouble_Truncates()
        => Assert.Equal(3, ConfigValues.GetInt(Config("k", 3.0), "k"));

    [Fact]
    public void GetInt_FromJsonElement_Reads()
        => Assert.Equal(5, ConfigValues.GetInt(Config("k", Json("5")), "k"));

    [Fact]
    public void GetInt_FromNumericString_Reads()
        => Assert.Equal(7, ConfigValues.GetInt(Config("k", "7"), "k"));

    [Fact]
    public void GetInt_Missing_ReturnsFallback()
        => Assert.Equal(2, ConfigValues.GetInt(new Dictionary<string, object>(), "k", 2));

    [Fact]
    public void GetDouble_FromJsonElement_Reads()
        => Assert.Equal(0.75, ConfigValues.GetDouble(Config("k", Json("0.75")), "k"));

    [Fact]
    public void GetBool_BoxedTrue_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", true), "k"));

    [Fact]
    public void GetBool_FromJsonElementTrue_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", Json("true")), "k"));

    [Fact]
    public void GetBool_FromString_Reads()
        => Assert.True(ConfigValues.GetBool(Config("k", "true"), "k"));

    [Fact]
    public void GetBool_FromNonZeroNumber_IsTrue()
        => Assert.True(ConfigValues.GetBool(Config("k", 1.0), "k"));

    [Fact]
    public void GetBool_Missing_ReturnsFallback()
        => Assert.True(ConfigValues.GetBool(new Dictionary<string, object>(), "k", true));

    [Fact]
    public void AsString_Null_ReturnsEmpty()
        => Assert.Equal(string.Empty, ConfigValues.AsString(null));

    [Fact]
    public void TryAsDouble_NonNumericString_ReturnsFalse()
        => Assert.False(ConfigValues.TryAsDouble("abc", out _));
}
