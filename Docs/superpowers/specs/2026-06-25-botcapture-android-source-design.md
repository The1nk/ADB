# BotCapture: Android device as a capture source

**Date:** 2026-06-25
**Status:** Approved (design)
**Branch:** `feat/botcapture-android-source`

## Problem

BotCapture — the tool that grabs a screenshot, lets the user crop a region, and saves it as a PNG template for "Find Image" actions — only captures **Win32 windows**. Its entire pipeline is keyed on a window `HWND` (`IntPtr`): the picker (`WindowPickerViewModel`), the live **Test Match** (`PreviewConfirmViewModel.TestMatch`), and the standalone **Retest** (`SessionViewModel.Retest`) all re-capture the source via `IWindowCapture.Capture(handle, …)`. An Android device has no HWND, so there has never been a way to capture an Android screen into a template — even though ADB devices are first-class automation targets and the **Android Find Image** action exists.

Clicking the **Capture** button on an Android Find Image action today launches BotCapture, which only offers a Windows window picker — never the connected device. This is the gap to close.

## Goal

Teach BotCapture to enumerate connected ADB devices as a capture source alongside Windows windows, so the existing **Capture** button (and standalone BotCapture) can grab a device screen, crop it, live-test the match, and save a template — with no change to the BotBuilder Capture button or `CaptureLauncher`.

## Correctness guarantee

`AndroidImageActionBase.CaptureAndMatch` matches templates against `IAndroidDevice.Screenshot()` — the ADB framebuffer, in device pixels. This design captures templates from the **same** `Screenshot()` path, so a saved template lands in exactly the device-pixel space the runtime matches in. No DPI/scaling translation is required. The Windows path remains PerMonitorV2 and is untouched.

## Design

### 1. Capture-source abstraction

Introduce a small, re-capturable source that both a window and an ADB device satisfy. This replaces the bare `IntPtr sourceHandle` (+ injected `IWindowCapture`) carried by the confirm/session/row path.

```csharp
public interface ICaptureSource
{
    string Label { get; }     // window title / device model-or-serial
    string SubLabel { get; }  // process name / "emulator-5554 · device"
    Bitmap Capture();         // fresh frame; caller owns and disposes
}
```

- `WindowCaptureSource(WindowInfo info, IWindowCapture capture)` → `Capture()` = `capture.Capture(info.Handle, ScreenCaptureMethod.Auto)`.
- `AndroidCaptureSource(string serial, IAndroidDevice device)` → `Capture()` decodes `device.Screenshot()` (PNG bytes) into a detached `Bitmap`. The device's existing serial re-resolve (`AdvancedSharpAdbDevice.Invoke`) means a reconnect/reboot self-heals on the next grab.

Location: `BotCapture.Core` (the sources depend on `AdbCore.Screen`/`AdbCore.Android` abstractions, which `BotCapture.Core` already references).

### 2. Source enumeration + adb availability

- **Device connector** `IAndroidDeviceConnector { IAndroidDevice Connect(string serial); }`.
  Live impl resolves the live `DeviceData` by serial via `AdbClient`/`GetDevices()` and returns `new AdvancedSharpAdbDevice(client, device)`. Injectable for tests.
- **Device listing** reuses the existing `IAdbDevices.List()` (`AdvancedSharpAdbDevices`), which starts the ADB server and returns `AdbDeviceInfo(Serial, State)`.
- **Availability is derived, not a new probe.** No dependency on `BotBuilder.Core.Palette.DependencyProbe`:
  - `List()` throws (server can't start because `adb.exe` isn't found) → reason: *"adb not found on PATH — install Android platform-tools."*
  - `List()` returns empty → reason: *"No devices connected — run `adb devices`, then Refresh."*
  - Only devices in the `device` state are offered (offline/unauthorized devices are shown disabled or excluded with the state visible). Capturing requires a usable device.

### 3. Picker UI — segmented toggle

`BotCapture/Views/WindowPickerView.xaml` gains a themed, keyboard-focusable **`Windows | Android`** segmented control above the list.

- The picker VM grows a `SourceKind` ({ `Window`, `Android` }) and rebuilds the active list when the kind changes.
- **Windows** rows: unchanged (title, process name, thumbnail).
- **Android** rows: device model-or-serial + serial + state, with a framebuffer thumbnail per connected device.
- **Android unavailable**: the inline reason (from §2) renders in place of the list; the `Capture` button is disabled.
- `Capture` calls `SelectedSource.Capture()` regardless of kind.

The VM (`WindowPickerViewModel`) generalizes to expose a `SelectedSource : ICaptureSource?` and `TakeCapturedImage()`; window-specific public surface is replaced by the source-kind-aware shape. Existing thumbnail/refresh/capture-failure-as-`StatusMessage` behavior is preserved for both kinds.

### 4. Wiring

`BotCapture/MainWindow.xaml.cs`:

- `_sourceHandle : IntPtr` → `_source : ICaptureSource`.
- `OnCaptureAccepted` reads `_pickerVm.SelectedSource`.
- `PreviewConfirmViewModel`, `SessionViewModel`, and `SessionRow` take an `ICaptureSource` in place of `(IntPtr sourceHandle, IWindowCapture capture)`. Their `TestMatch`/`Retest` re-grab via `source.Capture()`.
- Integrated single-shot mode (`--output`, the BotBuilder Capture button) and standalone session mode both light up, since they share the picker.
- **No change** to `CaptureLauncher` or the BotBuilder Capture button.

### 5. Testing

`BotCapture.Core.Tests`:

- Fakes: `IAdbDevices` (empty / throwing / populated), `IAndroidDeviceConnector` + `IAndroidDevice` returning a canned PNG, and a `FakeCaptureSource`.
- New coverage:
  - Toggling `SourceKind` rebuilds the active list.
  - Unavailable reasons (no adb vs. no devices) surface correctly.
  - `CaptureSelected` captures through `SelectedSource`.
  - `PreviewConfirmViewModel.TestMatch` / `SessionViewModel.Retest` re-grab through the injected `ICaptureSource`.
- Update existing `WindowPickerViewModelTests`, `SessionViewModelTests`, `PreviewConfirmViewModelTests` to the `ICaptureSource` shape.

## Out of scope

- Persisting the source across BotCapture restarts (session rows are in-memory only, as today).
- DXGI/Windows-Graphics-Capture backends (separate idea).
- Browser as a capture source.

## Slicing (for subagent-driven execution)

1. **Core slice** (`BotCapture.Core` + tests, fully unit-testable): `ICaptureSource` + `WindowCaptureSource` + `AndroidCaptureSource`, `IAndroidDeviceConnector` (+ live impl), VM refactor to `ICaptureSource`/`SourceKind`, and all unit tests.
2. **WPF slice** (`BotCapture` views/window): segmented toggle XAML + device rows + unavailable hint + `MainWindow` wiring.

Shipped as one branch/PR and **parked for user visual verification + merge** (visual slice).
