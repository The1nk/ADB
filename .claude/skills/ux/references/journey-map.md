# Journey Map Reference

## Contents
- What is a Journey Map in ADB
- Editor Canvas Journey
- Dialog Journey (Picker Dialogs)
- Run Execution Journey
- DO / DON'T Pairs
- Checklist

## What is a Journey Map in ADB

Before coding any interactive surface, document: **user intent → preconditions → success state → failure state → recovery path**. ADB surfaces are: canvas editing, picker dialogs, properties panel, palette, and the run toolbar.

## Editor Canvas Journey

| Stage | Detail |
|-------|--------|
| Intent | Connect actions into a runnable bot |
| Precondition | At least one node exists on canvas |
| Success | Connection drawn; `ConnectionViewModel` added; undo stack updated |
| Failure | Invalid port type mismatch; connection attempt rejected |
| Recovery | Visual feedback on port hover (highlight valid targets); no partial state left on canvas |
| Empty state | "Drag an action from the palette to get started" centered on canvas |

Key files: `BotBuilder.Core/BotEditorViewModel.cs`, `BotBuilder.Core/CanvasViewport.cs`, `BotBuilder.Core/ConnectionViewModel.cs`

## Dialog Journey — Picker Dialogs

Applies to: `TargetPickerDialog.xaml`, `SelectorPickerDialog.xaml`, `CoordinatePickerDialog.xaml`, `RegionPickerDialog.xaml`

| Stage | Detail |
|-------|--------|
| Intent | Select a window/device/selector/region |
| Precondition | Device connected (Android) or window open (Windows) |
| Success | Selection confirmed; dialog closes; parent VM receives value |
| Failure | No devices found; window closed; ADB not on PATH |
| Recovery | Retry button visible; error message names the problem; dialog stays open |
| Cancel | No mutation; parent state unchanged |

```csharp
// new code to add — enforce no-mutation-on-cancel contract
protected override void OnCancel()
{
    // Do NOT write back to parent VM here
    DialogResult = false;
}

protected override void OnConfirm()
{
    if (SelectedItem is null) return; // guard: no silent empty confirm
    ParentViewModel.ApplySelection(SelectedItem);
    DialogResult = true;
}
```

## Run Execution Journey

Key files: `BotBuilder.Core/Integration/RunStatusTracker.cs`, `BotBuilder.Core/Integration/RunCommandBuilder.cs`

| Stage | Detail |
|-------|--------|
| Intent | Test-run the bot from the editor (F5) |
| Precondition | At least one node; targets resolved |
| Pending | Toolbar shows stop button; canvas nodes highlight active node |
| Success | Run log shows completion; toolbar resets to Run |
| Failure | Run log shows error node + message; canvas highlights failed node |
| Recovery | User can fix action config and re-run; no orphaned BotRunner process |

```csharp
// Existing pattern from RunStatusTracker — bind IsRunning to UI
public bool IsRunning { get; private set; }
public string? LastError { get; private set; }
```

## DO / DON'T Pairs

**DO** — keep dialog open on error so the user can retry:
```xml
<!-- new code to add -->
<Button Content="Retry" Command="{Binding RefreshCommand}"
        Visibility="{Binding State, Converter={StaticResource ErrorToVisible}}" />
```

**DON'T** — close dialog on failure and silently discard the user's intent:
```csharp
// BAD — closes dialog even when the operation failed
catch { DialogResult = false; }
```

**DO** — restore prior value when cancel is pressed:
```csharp
// new code to add
private string _savedValue = string.Empty;
public void BeginEdit() => _savedValue = CurrentValue;
public void CancelEdit() => CurrentValue = _savedValue;
```

**DON'T** — mutate the parent VM during dialog interaction before confirm.

## Journey Map Checklist

Copy and track for every new dialog or canvas feature:

- [ ] Intent documented (one sentence)
- [ ] Preconditions listed (what must be true before the user can act)
- [ ] Success state defined (what the user sees, what state changes)
- [ ] Failure state defined (what went wrong, shown to user without leaking internals)
- [ ] Recovery path exists (retry button, error message, dialog stays open)
- [ ] Cancel path leaves no mutation
- [ ] Empty state covered if collection-based UI