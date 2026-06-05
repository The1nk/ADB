# Math Action — Design

**Status:** Approved (design decisions confirmed with user 2026-06-05)
**Context:** Polish item (e) from the post-V1 roadmap, expanded. There is currently no way to do arithmetic on run variables — `Set Variable` only does `${var}` substitution, not evaluation, and Lua (M12) is heavy-handed for `i += 1`. This adds a lightweight **visual** math node covering arithmetic plus common standard-library functions (rounding, abs, sqrt, min/max, power, randomness), consistent with V1's "common simple ops stay visual" philosophy.

---

## 1. Overview

A single **Math** built-in action (`data.math`, **Data** category) computes a value from an `operation` and up to two operands, and stores the numeric result in a run variable. It sits next to `Set Variable` in the palette and is the discoverable answer to "increment a counter", "round a value", "pick a random delay" without reaching for Lua.

## 2. Configuration

| Field | Key | Type | Notes |
|---|---|---|---|
| Operation | `operation` | Enum | one of the 16 operations below; default `Add` |
| Left | `left` | String | number literal (`5`) or `${var}`; the sole operand for unary ops; the min for `RandomInt` |
| Right | `right` | String | number literal or `${var}`; the second operand for binary ops; the max for `RandomInt`; ignored by unary/`Random` |
| Result Variable | `resultVariable` | String | bare variable name the result is written to |

**`${var}` is resolved upstream.** `BotExecutor` runs `ConfigInterpolator.Resolve` before every action, so `${count}` in `left`/`right` is already replaced with the variable's string form by the time `Math` executes. The action only parses the (already-interpolated) operand strings as numbers — it does not interpolate itself. (Unit tests use literal numeric operands since they call `ExecuteAsync` directly; `${var}` support is provided/tested by the existing `ConfigInterpolator`.)

## 3. Operations (16)

Public string constants (mirroring `BranchAction`'s operator constants), grouped by arity:

- **Binary** (parse `left` AND `right`): `Add` (a+b), `Subtract` (a−b), `Multiply` (a×b), `Divide` (a/b), `Modulo` (a%b), `Power` (`Math.Pow`), `Min` (`Math.Min`), `Max` (`Math.Max`).
- **Unary** (parse `left` only; `right` ignored): `Floor` (`Math.Floor`), `Ceil` (`Math.Ceiling`), `Round` (`Math.Round`, `MidpointRounding.AwayFromZero` so `Round(2.5)=3`), `Abs` (`Math.Abs`), `Sqrt` (`Math.Sqrt`), `Negate` (−a).
- **Random** (no operands): `Random` → uniform double in `[0,1)` via `Random.Shared.NextDouble()`.
- **RandomInt** (parse `left`=min, `right`=max): uniform integer in `[min,max]` **inclusive**, via `Random.Shared.NextInt64(lo, hi+1)`, stored as a `double`. `min`/`max` are taken as `(long)Math.Round(operand, AwayFromZero)`.

Default operation: `Add`.

## 4. Ports

- `in` (input), `onSuccess`, `onFailure`.
- Success → `onSuccess`. Any computation error → `onFailure` with a clear message.

## 5. Semantics

- Operands parsed with `double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, ...)` (matching `BranchAction`).
- The result is stored as a **`double`** in `ExecutionContext.Variables[resultVariable]` (the canonical number type; readers coerce).
- `Random.Shared` is used for randomness — it is thread-safe, so the action is safe under `Run Parallel` (no shared-mutable-RNG hazard).

## 6. Error model (→ `onFailure`, prefix `Math: `)

- Non-numeric `left` (any op that needs it) → `Math: left operand '<text>' is not a number`.
- Non-numeric `right` (any binary op / `RandomInt`) → `Math: right operand '<text>' is not a number`.
- **Divide by zero** (`Divide`, right == 0) → `Math: divide by zero`.
- **Modulo by zero** (`Modulo`, right == 0) → `Math: modulo by zero`.
- **RandomInt** with `min > max` → `Math: RandomInt requires left (min) <= right (max)`.
- Empty `resultVariable` → `Math: result variable name is required`.
- **Non-finite result** (`double.IsNaN` or `double.IsInfinity`) for any op — e.g. `Sqrt` of a negative, `Power` overflow → `Math: result is not a finite number` (rather than silently writing `NaN`/`Infinity` into a variable). Note: `Divide`/`Modulo` by zero are caught by their explicit checks above before this general guard, so they get their specific messages.

Per the user's decision, div/mod by zero (and other non-finite outcomes) are explicit failures, not silent values — a bot catches a real arithmetic mistake via the `onFailure` path.

## 7. `SupportsRetry`

`false` — deterministic ops re-run identically; random ops would re-roll but that's not a "retry until success" semantic. (Matches `Set Variable` / `Branch`.)

## 8. Architecture / files

- `AdbCore/Actions/BuiltIn/MathAction.cs` — implements `IActionDefinition` + `IActionExecutor`, mirroring `BranchAction` (enum config field with `Options`, `ConfigValues.GetString`, `double.TryParse` with `InvariantCulture`). No new infrastructure. A private `Compute(op, left, right, out result, out error)` keeps the arity/op dispatch readable.
- Registered in `BuiltInActions.cs` alongside the other no-external-dependency Data actions (`Set Variable`).
- Counts bump +1 def / +1 exec; palette **Data** category +1; `PaletteViewModelTests` total +1.

## 9. Testing

Deterministic unit tests (no external deps) → backend-only, **self-mergeable**:
- Each binary op with literal operands → correct `double` in the result variable, routes `onSuccess` (e.g. `Add 2,3 → 5`; `Power 2,10 → 1024`; `Min 3,7 → 3`; `Modulo 7,3 → 1`).
- Each unary op → correct value (`Floor 2.9 → 2`; `Ceil 2.1 → 3`; `Round 2.5 → 3`; `Abs -4 → 4`; `Sqrt 9 → 3`; `Negate 5 → -5`).
- `Random` → result in `[0,1)` (assert bounds; loop a handful of times).
- `RandomInt` → integer in `[min,max]` inclusive (loop several iterations; assert each is integral and in-range; cover the `min==max` single-value case).
- `Divide`/`Modulo` by zero → `onFailure` + the specific message.
- `Sqrt` of negative → `onFailure` + "not a finite number".
- `RandomInt` with min > max → `onFailure`.
- Non-numeric operand → `onFailure` + "is not a number".
- Empty result variable → `onFailure`.
- `Definition_Metadata`: TypeKey `data.math`, DisplayName `Math`, Category `Data`, ports `onSuccess`/`onFailure`, the `operation` enum field carries all 16 options, `left`/`right`/`resultVariable` string fields exist.
- Registration test: `data.math` resolves in both registries.

## 10. Out of scope

- Multi-term expressions / a full expression parser (single op covers `i += 1` and "compute one value"; expression-eval was the rejected alternative).
- Trig / log / other transcendental functions beyond `Sqrt`/`Power` (add later if wanted).
- Seeded/deterministic randomness for reproducible runs (uses `Random.Shared`; revisit only if a "seed" need appears).
- Integer-specific storage (everything is `double`, consistent with the variable system; `RandomInt`/`Floor`/etc. simply produce integral doubles).

## 11. Merge handling

Backend-only AdbCore action with deterministic unit tests, no live deps, no custom UI (renders via the generic node/properties templates) → built compile-clean + unit-green and **self-merged** via `gh` (backend-only-slice rule, user-authorized). Independent of the open PR #34 (isolated to `AdbCore/Scripting/**`) — no shared files.
