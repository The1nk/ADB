using AdbCore.Android;

namespace AdbCore.Tests.Android;

public class AdbSelectorTests
{
    [Theory]
    [InlineData("serial:emulator-5554", "emulator-5554")]
    [InlineData("SERIAL:ABC123", "ABC123")]
    public void ParseSerial_ReturnsSerial(string selector, string expected)
        => Assert.Equal(expected, AdbSelector.ParseSerial(selector));

    [Theory]
    [InlineData("process:BlueStacks")]
    [InlineData("serial:")]
    [InlineData("emulator-5554")]
    public void ParseSerial_NonSerial_ReturnsNull(string selector)
        => Assert.Null(AdbSelector.ParseSerial(selector));
}
