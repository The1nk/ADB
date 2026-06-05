# Type-Aware Target Defaulting — Design

**Status:** Approved (Polish item b, runtime variant)
**Context:** Each targeted action (Android / Window / Browser) resolves the live handle it acts on. When a node has no explicit `TargetId`, the current code defaults to the sole target **only when there is exactly one target total** (`targets.Count == 1`). So a bot with, e.g., **1 Android + 1 Window** target leaves every unassigned type-specific node unresolved → it fails at runtime with "requires a …". This fixes the defaulting to be **type-aware**: an unassigned node defaults to the single target whose live handle matches the action's type.

---

## 1. Problem

Five resolution sites contain the identical block:
```csharp
ResolvedTarget? target = context.Action.TargetId is Guid id
    ? targets.TryGetValue(id, out var t) ? t : null
    : targets.Count == 1 ? targets.Values.First() : null;
return target?.Handle as <HandleType>;
```
- `AndroidActionBase.ResolveDevice` → `IAndroidDevice`
- `ScreenActionBase.ResolveWindow` → `IntPtr` (HWND)
- `InputActionBase.ResolveWindow` → `IntPtr` (HWND)
- `ScreenOcrActionBase.ResolveWindow` → `IntPtr` (HWND)
- `BrowserActionBase.ResolvePage` → `IBrowserPage`

The `Count == 1` default ignores the handle type, so multi-target bots fail.

## 2. Fix

A single shared helper resolves by handle type:

```csharp
namespace AdbCore.Execution;

public static class TargetResolution
{
    /// <summary>The handle of type <typeparamref name="T"/> the action should act on: the explicit
    /// TargetId's handle when set (and of type T), or — when TargetId is unset — the single target whose
    /// live handle is a T. Returns default (null/none) when there is no match, or when the type-default is
    /// ambiguous (zero, or more than one, target of that type).</summary>
    public static T? ResolveHandle<T>(ActionExecutionContext context)
    {
        var targets = context.Context.Targets;
        if (context.Action.TargetId is Guid id)
            return targets.TryGetValue(id, out var t) && t.Handle is T match ? match : default;

        T? found = default;
        var count = 0;
        foreach (var target in targets.Values)
            if (target.Handle is T handle) { found = handle; count++; }
        return count == 1 ? found : default;
    }
}
```

Each of the five sites becomes a one-liner:
- Android: `TargetResolution.ResolveHandle<IAndroidDevice>(context)`
- Window (Screen / Input / ScreenOcr): `TargetResolution.ResolveHandle<IntPtr>(context)` (returns `IntPtr?`)
- Browser: `TargetResolution.ResolveHandle<IBrowserPage>(context)`

`ResolvedTarget.Handle` is `object?`; `Handle is T` matches reference handles (`IAndroidDevice`, `IBrowserPage`) and the boxed value handle (`IntPtr`), and a null handle (unbound target) never matches — so an unbound target is correctly ignored.

## 3. Behavior

- **Explicit `TargetId`:** unchanged — resolves that target's handle if it is the right type, else none (same as the old `Handle as T`).
- **No `TargetId`, exactly one target of the matching type:** resolves it (NEW — fixes multi-target bots).
- **No `TargetId`, single target total of the matching type:** resolves it (same as before for single-target bots — no regression).
- **No `TargetId`, zero or ≥2 targets of the matching type:** none (unchanged for the zero case; the ≥2 case stays ambiguous-by-design and the action fails with its existing "requires a …" message, prompting explicit assignment).

No working bot regresses: any bot that resolved under the old `Count == 1` rule had exactly one target, which is necessarily the single target of its type (or a type mismatch that failed before and still fails).

## 4. Out of scope

- The **editor-side** convenience of auto-assigning `TargetId` on node-add (the other half of Polish item b) — this runtime fix makes unassigned nodes "just work" for the common single-per-type case, which is the higher-value half; the editor auto-assign can follow separately.
- Changing the ≥2-of-type case to pick one (intentionally left ambiguous → fail, so the user assigns explicitly).

## 5. Testing

Deterministic unit tests for `TargetResolution` (no external deps), plus the existing action-base tests must stay green:
- 1 Android + 1 Window, unassigned → `ResolveHandle<IAndroidDevice>` returns the Android handle; `ResolveHandle<IntPtr>` returns the window HWND. (The fix.)
- 1 Browser among mixed → `ResolveHandle<IBrowserPage>` returns it.
- 2 Android, unassigned → `ResolveHandle<IAndroidDevice>` returns null (ambiguous).
- 0 of type → null.
- Explicit TargetId, right type → that handle; wrong type → null; missing id → null.
- Single target total (back-compat) → resolves.

## 6. Merge handling

Backend-only (AdbCore runtime resolution) with deterministic unit tests and no UI surface. However it **changes runtime target-resolution semantics**, so it is opened as a PR and **user-verified + merged** (a quick check that a real 1-Android-1-Window bot now runs unassigned nodes) rather than self-merged. Independent of open PRs #34 (Scripting) — no shared files.
