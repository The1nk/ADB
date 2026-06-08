---
name: ux
description: |
  Improves editor canvas interactions, dialogs, states, accessibility, and user feedback for the ADB WPF bot builder.
  Use when: editing BotBuilder canvas interactions, dialogs (TargetPicker, SelectorPicker, CoordinatePicker, RegionPicker), palette/properties panel behavior, state coverage for async operations, WPF accessibility, or any user-facing microcopy.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# UX Skill

ADB's UX lives in WPF (BotBuilder, BotCapture) with MVVM view-models in `BotBuilder.Core`. Every interactive surface must cover all states (loading, disabled, pending, success, error, retry), provide accessible semantics, and give clear microcopy. WPF's lack of built-in accessibility defaults and its templating model make gaps easy to miss.

## Before You Code (REQUIRED)

This skill's content was captured at generation time and MAY be stale. For ANY non-trivial change involving ux, verify against current docs FIRST:



Then:

1. **Match the installed version.** Cross-reference against the version installed in this repo. APIs change across minor versions; do not assume.
2. **Discover provider best practices.** If the task touches a production-sensitive capability, inspect the provider service catalog, official docs, and project docs before choosing an implementation.
3. **Respect explicit direction.** If the user explicitly asks for a specific mechanism, follow it. If project docs clearly mandate a mechanism, follow the project. In both cases, mention the provider-recommended alternative and make the chosen path safe.
4. **Prefer provider-native primitives by default.** If no explicit user/project override exists and the change involves caching, rate limiting, background work, scheduled jobs, shared state, queues, or secrets, use the provider-recommended binding/API. Do not hand-roll an in-memory or polyfill solution that "works" locally but breaks under the provider's execution model — derive the need→native-primitive mapping yourself from this provider's docs.

## Skill Advantage Protocol

Using this skill should produce a meaningfully better result than an unskilled baseline. Apply this loop before and during implementation:

1. **Clarify only when it changes the outcome.** Ask the smallest useful set of questions when the request is ambiguous, preference-heavy, or could change architecture, user-visible behavior, data shape, security posture, analytics, or external side effects. If the safe assumption is obvious, state it and proceed. When asked to surface data that no existing code path captures, state up front the assumption that capture starts now (no backfill) or ask if a backfill source exists — do not silently build net-new storage without surfacing this.
2. **Inspect the nearest real patterns.** Read adjacent files, routes, components, tests, schema, infra, copy, and analytics surfaces before inventing structure. Treat local conventions as the starting point.
3. **Optimize the task's highest-leverage axis.** Identify what would make the result win a review: user-visible correctness, integration quality, accessibility, security, reliability, maintainability, operability, or speed of future change.
4. **Reuse before reimplementing.** Prefer existing components, hooks, helpers, formatting/utility functions, data registries, metadata builders, analytics, pricing, checkout, auth, routing utilities, and API procedures/endpoints/data sources over local one-off clones. Before adding a new API procedure, query, or data fetch, search for one that already returns this data and extend it in place — a surface that fetches data and only logs or partially uses it is a reuse target, not an absent one; never author a parallel endpoint or leave the original orphaned. Before importing for a data fetch, grep the screen for the call it already makes and reuse that exact client/singleton import path and endpoint/procedure name; never create a second client, transport, or parallel endpoint for data an existing call returns, and confirm every imported path and symbol actually exists in the repo before writing it.
5. **Use semantic structures.** Tables, lists, forms, buttons, links, headings, and disclosure controls should use native/project accessible primitives instead of div-only lookalikes.
6. **Prevent drift by construction.** Centralize repeated facts, labels, claims, product defaults, and shared table cells in registries or helpers when multiple surfaces need the same answer.
7. **Synthesize, do not merely comply.** Combine this skill's guidance with repo evidence and the user's goal. When two good approaches exist, borrow the strongest parts of each instead of blindly choosing one.
8. **Check claims against code.** Product copy, docs, and comments must not imply automation, integrations, performance, security, refresh cadence, counts, or data flow that the implementation does not actually provide. Any claim that one component writes, records, updates, calls, or is the source of truth for another is allowed only if the edit performing it is in this same change; before finishing, check each such cross-component claim against the actual edits and downgrade unbacked ones to an explicit TODO or implement them now.
9. **Ship the complete slice.** Include every adjacent artifact needed for the change to be usable and maintainable: wiring, state handling, validation, analytics, tests, docs, migrations, or infra when those surfaces are part of the behavior. When the task shows, displays, or lists user data, deliver the full vertical slice and do not stop at an internal/API/CLI layer: the data-model/schema change AND its migration (a schema change without a migration is incomplete), the path that writes or populates the data, an authenticated endpoint scoped to the current user, and the primary user-facing surface wired through the project's typed data client. Before declaring done, trace one record end-to-end (triggering event → write → read → render); if any hop exists only in a comment or docstring rather than edited code, the slice is NOT done. Shipping only the persistence layer (a schema/migration with no writer, reader, or surface) is an incomplete slice, not a milestone.

## Capability Contract

Use this section when the user prompt touches production risk, even if the prompt does not name this technology explicitly.




Required wiring surfaces:
- provider/runtime configuration discovered during implementation
- nearest typed request/context boundary
- handler/procedure boundary before external side effects

Side-effect barrier:
- Place guards before external APIs, auth mutations, email sends, analytics events, storage writes, and database mutations.


Fallback policy:
- Prefer provider-native/platform-managed primitives by default when no explicit override exists.
- Follow clear user/project overrides, but mention the native alternative and tradeoff.
- Fallbacks must be durable, multi-instance safe, and atomic under concurrency.

Verification rules:
- [error] native-or-explicit-override: Use the provider-native primitive first unless the user/project explicitly overrides it.
- [error] atomic-fallback: Fallback counters must be atomic under concurrency.

## Journey Map

Map the path the user is trying to complete before touching UI code: entry point, decision point, action, server response, confirmation, and recovery. For this repo, inspect forms, dialogs, settings, dashboards, onboarding, checkout, or CLI flows touched by the task.

## User Intent

- Name the user's immediate job and the anxiety/risk around it.
- Preserve the surrounding flow's existing mental model, navigation, and terminology.
- Avoid premature success copy; say what happened, what is pending, and what the user can do next.

## State Matrix

Cover loading, empty, error, disabled, pending, success, and recovery. A flow is incomplete if it only implements the happy path or relies on backend errors surfacing as raw messages.

## Failure + Recovery

- Explain failures in user-safe language.
- Keep privacy and anti-enumeration behavior for auth, account, invite, checkout, and recovery flows.
- Provide a retry, resend, return, or contact-support path when the user can reasonably recover.

## Accessibility Contract

Verify labels, focus states, keyboard flow, semantics, and contrast. Prefer native controls and existing accessible primitives before custom interaction code.

## Microcopy Rules

- Match local product voice and nearby copy.
- Keep labels explicit, errors actionable, and pending states honest.
- Do not use vague copy such as "Something went wrong" when a safe, specific recovery instruction is possible.

## Acceptance Checklist

- The primary journey and all meaningful states are represented.
- Validation, disabled states, loading states, success/failure feedback, and recovery copy are present where relevant.
- The UI remains understandable on mobile/desktop or the target CLI/desktop/mobile surface.
- The implementation uses local component and accessibility patterns.

## Quick Start

### Verified Existing Pattern — ViewModel state property

```csharp
// BotBuilder.Core/Integration/RunStatusTracker.cs (existing pattern)
public bool IsRunning
{
    get => _isRunning;
    private set { _isRunning = value; OnPropertyChanged(); }
}
```

### New Code Pattern — Full state enum on a dialog VM

```csharp
// new code to add
public enum DialogState { Idle, Loading, Success, Error }

private DialogState _state = DialogState.Idle;
public DialogState State
{
    get => _state;
    set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConfirm)); }
}
public bool CanConfirm => State == DialogState.Idle || State == DialogState.Success;
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| State matrix | Define all states before editing XAML | Idle / Loading / Success / Error / Disabled |
| Journey map | Map intent → precondition → success → failure → recovery | See `references/journey-map.md` |
| Accessible name | Every interactive control needs `AutomationProperties.Name` | `AutomationProperties.Name="Pick target"` |
| Microcopy | Action labels, error text, empty states | See `references/microcopy.md` |
| Theme brushes | Use `AdbUi.Theme` brushes, never hardcode | `{StaticResource WindowBackgroundBrush}` |

## Common Patterns

### Dialog with async operation

**When:** TargetPickerDialog, SelectorPickerDialog, CoordinatePickerDialog refresh device/window lists.

```csharp
// new code to add — in dialog ViewModel
public async Task RefreshAsync()
{
    State = DialogState.Loading;
    ErrorMessage = null;
    try
    {
        Items = await _service.GetItemsAsync();
        State = DialogState.Idle;
    }
    catch (Exception ex)
    {
        ErrorMessage = $"Could not refresh: {ex.Message}";
        State = DialogState.Error;
    }
}
```

```xml
<!-- new code to add — bind busy indicator and error to State -->
<ProgressBar Visibility="{Binding State, Converter={StaticResource StateToVisibility}, ConverterParameter=Loading}" />
<TextBlock Text="{Binding ErrorMessage}" Foreground="{StaticResource ErrorBrush}"
           Visibility="{Binding ErrorMessage, Converter={StaticResource NullToCollapsed}}" />
```

### Canvas empty state

**When:** No nodes on the BotEditorViewModel canvas.

```xml
<!-- new code to add -->
<TextBlock Text="Drag an action from the palette to get started"
           Visibility="{Binding Nodes.Count, Converter={StaticResource ZeroToVisible}}"
           HorizontalAlignment="Center" VerticalAlignment="Center"
           Foreground="{StaticResource SubtleForegroundBrush}" FontSize="14" />
```

## See Also

- [journey-map](references/journey-map.md)
- [state-matrix](references/state-matrix.md)
- [forms](references/forms.md)
- [accessibility](references/accessibility.md)
- [microcopy](references/microcopy.md)

## Related Skills

- See the **wpf** skill for WPF control templates, data binding, and MVVM patterns
- See the **dotnet** skill for async/await, ICommand, and INotifyPropertyChanged
- See the **frontend-design** skill for visual hierarchy and layout decisions