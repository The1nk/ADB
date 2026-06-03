using AdbCore.Models;
using BotBuilder.Core.Integration;

namespace BotBuilder.Core.Tests.Integration;

public class TargetPickerViewModelTests
{
    private static TargetPickerViewModel Make() => new(
        "BotRunner.exe",
        @"C:\farm.bot",
        new[]
        {
            ("Client 1", BotTargetType.Window, "process:BlueStacks"),
            ("My Phone", BotTargetType.AndroidDevice, "serial:emulator-5554"),
        });

    [Fact]
    public void Rows_MirrorTargets_WithWindowFlag()
    {
        var vm = Make();

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("Client 1", vm.Rows[0].Name);
        Assert.True(vm.Rows[0].IsWindow);
        Assert.False(vm.Rows[1].IsWindow);
        Assert.Equal("serial:emulator-5554", vm.Rows[1].Selector);
    }

    [Fact]
    public void CommandPreview_ReflectsCurrentSelectors()
    {
        var vm = Make();

        Assert.Equal(
            "BotRunner.exe --bot C:\\farm.bot --target \"Client 1=process:BlueStacks\" --target \"My Phone=serial:emulator-5554\"",
            vm.CommandPreview);
    }

    [Fact]
    public void EditingSelector_RaisesCommandPreviewChange()
    {
        var vm = Make();
        var changed = false;
        vm.PropertyChanged += (_, e) => changed |= e.PropertyName == nameof(vm.CommandPreview);

        vm.Rows[0].Selector = "hwnd:12345";

        Assert.True(changed);
        Assert.Contains("Client 1=hwnd:12345", vm.CommandPreview);
    }

    [Fact]
    public void Selectors_ReturnsNameSelectorPairs()
    {
        var vm = Make();
        var pairs = vm.Selectors();

        Assert.Equal(("Client 1", "process:BlueStacks"), pairs[0]);
    }
}
