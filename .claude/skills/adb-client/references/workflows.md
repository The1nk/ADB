# ADB Client Workflows Reference

## Contents
- Adding a New Android Action
- Testing Android Actions
- Target Binding Workflow
- Debugging ADB Connectivity

---

## Adding a New Android Action

Copy this checklist and track progress:

- [ ] Create `AdbCore/Actions/BuiltIn/Android/MyAction.cs` implementing `IActionExecutor`
- [ ] Create `AdbCore/Actions/BuiltIn/Android/MyActionDefinition.cs` implementing `IActionDefinition`
- [ ] Register the definition in the action registry (verify registration pattern in existing Android actions)
- [ ] Add config fields via `IActionDefinition.Fields` — never hardcode values in the executor
- [ ] Write a unit test in `AdbCore.Tests/` using a `FakeAndroidDevice`
- [ ] Run `dotnet test ADB.slnx` — all tests must pass
- [ ] Verify action appears in BotBuilder palette under the Android category

**Iterate-until-pass:**
1. Implement action
2. Validate: `dotnet test ADB.slnx`
3. If tests fail, fix and repeat step 2
4. Only proceed to visual verification when all tests pass

---

## Testing Android Actions

Android executors must be testable without a real device. Use a hand-rolled fake (per project convention — no mock frameworks):

```csharp
// new code to add — AdbCore.Tests/Android/FakeAndroidDevice.cs
internal sealed class FakeAndroidDevice : IAndroidDevice
{
    public List<(int X, int Y)> Taps { get; } = new();
    public bool IsOffline { get; set; }

    public Task TapAsync(int x, int y, CancellationToken ct)
    {
        if (IsOffline) throw new AdbException("Device offline");
        Taps.Add((x, y));
        return Task.CompletedTask;
    }

    public Task<byte[]> ScreenshotAsync(CancellationToken ct) => Task.FromResult(Array.Empty<byte>());
}
```

```csharp
// new code to add — test using the fake
[Fact]
public async Task TapAction_ExecuteAsync_RecordsTap()
{
    var fake = new FakeAndroidDevice();
    var ctx = FakeBotExecutionContext.WithAndroidTarget("Main", fake);
    var action = new TapAction();

    var result = await action.ExecuteAsync(ctx, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Single(fake.Taps);
    Assert.Equal((100, 200), fake.Taps[0]);
}
```

See the **xunit** skill for context on fake-over-mock conventions in this project.

---

## Target Binding Workflow

When a bot run starts, `BotExecutionContext` is populated with resolved targets. The `serial:` prefix tells the resolver to connect an `IAndroidDevice`:

```
BotRunner CLI:  --target Main=serial:emulator-5554
BotBuilder UI:  TargetPickerDialog → selects Android device → stores "serial:<id>"
                                        ↓
                        AdbCore/Targets/ resolves to IAndroidDevice
                                        ↓
                        ctx.GetTarget<IAndroidDevice>("Main")
```

**DO:** Always use the target name from `IActionDefinition.TargetNames` — hardcoding `"Main"` across all actions assumes a single-target bot.

**DON'T:** Enumerate ADB devices inside an action executor. Device resolution happens once at bot startup; re-enumerating mid-run causes race conditions and latency spikes.

---

## Debugging ADB Connectivity

When `ctx.GetTarget<IAndroidDevice>(name)` returns null at runtime:

1. Confirm `adb devices` lists the device on PATH — ADB server must be running.
2. Check the target binding string matches `serial:<device-id>` exactly (case-sensitive).
3. For emulators, confirm the emulator port (`emulator-5554`) matches `adb devices` output.
4. USB devices: verify USB debugging is enabled and the authorization prompt was accepted on-device.

```bash
# Quick connectivity check
adb devices -l
# Expected: device serial listed with "device" status, not "offline" or "unauthorized"
```

If the device shows `offline`, ADB server restart usually fixes it:

```bash
adb kill-server
adb start-server
adb devices
```

**WARNING:** Never add retry loops inside action executors for ADB reconnection. Fail fast, return `ActionResult.Failure`, and let the user fix the device state. Silent retries mask real hardware problems and cause bots to hang indefinitely.