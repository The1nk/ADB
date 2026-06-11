using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class BotPropertiesViewModelTests
{
    [Fact]
    public void Exposes_EditableNameAndDescription()
    {
        var vm = new BotPropertiesViewModel("Bot", "Desc", DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal("Bot", vm.Name);
        Assert.Equal("Desc", vm.Description);
        vm.Name = "Renamed";
        Assert.Equal("Renamed", vm.Name);
    }

    [Fact]
    public void FormatsTimestamps_AndShowsDashForDefault()
    {
        var when = new DateTime(2031, 5, 6, 7, 8, 0, DateTimeKind.Utc);
        var vm = new BotPropertiesViewModel("B", "", when, default);
        Assert.Contains("2031", vm.CreatedDisplay);
        Assert.Equal("—", vm.UpdatedDisplay); // em dash for an unset timestamp
    }
}
