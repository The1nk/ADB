using BotBuilder.Core;
using Xunit;

namespace BotBuilder.Core.Tests;

public class UnsavedChangesGuardTests
{
    [Fact]
    public void NotDirty_ProceedsWithoutAsking()
    {
        var asked = false;
        var ok = UnsavedChangesGuard.ConfirmProceed(() => false, () => { asked = true; return SaveChoice.Cancel; }, () => true);
        Assert.True(ok);
        Assert.False(asked);
    }

    [Fact]
    public void Save_Succeeds_Proceeds()
        => Assert.True(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Save, () => true));

    [Fact]
    public void Save_Cancelled_Aborts()
        => Assert.False(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Save, () => false));

    [Fact]
    public void DontSave_Proceeds()
        => Assert.True(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.DontSave, () => false));

    [Fact]
    public void Cancel_Aborts()
        => Assert.False(UnsavedChangesGuard.ConfirmProceed(() => true, () => SaveChoice.Cancel, () => true));
}
