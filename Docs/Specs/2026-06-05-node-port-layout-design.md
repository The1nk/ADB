# Node Port Layout + Failure-Down Connectors — Design

**Status:** Approved (design confirmed with user 2026-06-05)
**Context:** Polish item (d) "make the parallel branches less hideous", broadened during brainstorming into a node port-layout pass. Two problems, one cohesive fix: (1) Run Parallel's branch ports spill off the bottom of a fixed-height card and trail as straight tails; (2) every node stacks all outputs down the right edge, so a failure path clutters the main flow. Fix: cards grow to fit their right-edge ports (with a centered block), and **failure outputs drop to the bottom edge** with direction-aware connectors. Entirely within `BotBuilder*` — no AdbCore/engine change.

---

## 1. The problem (today)

- `NodeLayout.CardHeight` is a fixed `70`. Ports are absolutely positioned in a `Canvas` overlay at `OutputAnchor(i) = (160, 40 + i*20)`. A `Canvas` doesn't size to its children, so any port past index 1 (y≥80) renders **below** the card with its connector trailing from empty space — the "runs off the node / dangles like a straight tail" look. Only Run Parallel (3+ branches) exceeds two outputs today.
- All outputs sit on the right edge, so `onFailure` lines up beside `onSuccess`, cluttering the main rightward flow.

## 2. Design

### 2a. Port edges
A port is assigned an **edge** (`PortEdge`: `Left` / `Right` / `Bottom`):
- **Inputs → Left.**
- **Failure outputs → Bottom.** A failure output is a port whose name is in the known set `{ "onFailure", "someFailed" }` (the matchable-action failure port + Join's `someFailed`). Identified by name in the editor layer (these are stable action constants), so the engine/AdbCore is untouched.
- **All other outputs → Right** (`onSuccess`, `out`, `true`/`false`, `branch1..N`, `allSucceeded`).

### 2b. Card height grows to fit the right-edge block
`Height = max(CardHeight_default(70), HeaderHeight + (rightCount−1)·PortSpacing + 2·BodyPad)`, where `rightCount` = number of right-edge outputs (NOT counting bottom/failure ports), `HeaderHeight = 28`, `PortSpacing = 20`, and `BodyPad ≈ 11` (tuned so 1- and 2-right-port nodes stay 70px). Bottom ports don't affect height. So matchable nodes (one right port = `onSuccess`) stay 70px; Run Parallel grows with its branch count.

### 2c. Centered blocks (uniform)
Each edge's ports form a block **centered on the card body** (the region below the header):
- `centerY = HeaderHeight + (Height − HeaderHeight) / 2`.
- **Right** port i of `rightCount`: `(CardWidth, centerY − (rightCount−1)·PortSpacing/2 + i·PortSpacing)`.
- **Left** input i of `inCount`: same centered Y math at `x = 0`. A single `in` lands at `centerY` — aligning to the middle of the right block.
- **Bottom** failure port j of `bottomCount`: centered horizontally along the bottom edge — `(CardWidth·(j+1)/(bottomCount+1), Height)`. A single `onFailure`/`someFailed` lands at `(CardWidth/2, Height)`.

This gently re-centers every node consistently (e.g. Branch's `in` now sits centered between True/False), which the user preferred, and grows Run Parallel.

### 2d. Direction-aware connectors
`ConnectionGeometry` currently pulls every curve horizontally. Make the control point at each endpoint extend along that port's edge **outward normal**:
- `Left → (−1, 0)`, `Right → (+1, 0)`, `Bottom → (0, +1)`.
- `C1 = start + pull · outward(sourceEdge)`, `C2 = end + pull · outward(targetEdge)`, with `pull = max(MinPull(40), distance(start,end)/2)`.

Effect: a right-edge `onSuccess` → left-edge input curve is identical to today (source pulls +X, target pulls −X). A bottom-edge `onFailure` leaves the card going **down**, then curves to its handler. (Inputs are always Left, so the target side is unchanged.)

## 3. Components changed (all `BotBuilder*`)

- **`PortEdge` enum** (new) + **`PortViewModel.Edge`** (new property).
- **`NodeLayout`** — `CardHeight(rightCount)`, centered `LeftAnchor/RightAnchor(index, count, height)`, `BottomAnchor(index, count)`, and `outward(edge)` normal. The failure-port name set lives here (or a small `PortRoles` helper).
- **`NodeViewModel`** — observable `Height`; `FromDefinition` classifies each output's edge, builds anchors via `NodeLayout`, sets `Height`; the Run Parallel branch builders (`BranchOutputPort`, `SetBranchPortCount`, `ReplaceOutputPorts`) and a `RecomputeLayout()` update all anchors + `Height` together on branch-count change.
- **`ConnectionGeometry`** — edge-aware `ControlPoints`/`BuildPath`.
- **`ConnectionViewModel`** — pass `SourcePort.Edge`/`TargetPort.Edge`; re-route `PathData` when an endpoint node's `Height` changes (in addition to `X`/`Y`), since a Run Parallel re-center moves its ports.
- **`MarqueeSelection`** — hit-test against per-node `Height` instead of the `CardHeight` const.
- **`BotEditorViewModel.OnBranchCountChanged` / `SetBranchCountCommand` / `DocumentMapper`** — run the same `RecomputeLayout` so ports/height/connections stay consistent on edit, undo, and load.
- **`MainWindow.xaml`** — bind the node `Border` height to `node.Height` (replace fixed `MinHeight=70`); ports already render by `AnchorOffset`, so bottom ports place themselves with no template change.

## 4. Behavior / compatibility

- **Existing right→left connectors look unchanged** (same horizontal pull).
- **`onFailure`/`someFailed` connectors** now leave the bottom and curve down — a visual change by design.
- **All nodes re-center** slightly and consistently (uniform centered blocks); 1- and 2-right-port nodes keep ~70px height.
- **Run Parallel** grows to fit N branches (the original bug fix) and centers them.
- No `.bot` file/schema change — anchors/heights are derived at render time from port names/counts; saved bots just re-render.

## 5. Testing (BotBuilder.Core.Tests — deterministic, no WPF)

- `NodeLayout`: `CardHeight` grows for rightCount≥3 and stays 70 for 1–2; right/left blocks are vertically centered & symmetric; single `in` lands at `centerY`; bottom anchor centers horizontally (and distributes for >1); `outward(edge)` normals.
- `PortViewModel`/`NodeViewModel`: `FromDefinition` assigns `Left` to inputs, `Bottom` to `onFailure`/`someFailed`, `Right` to the rest; `Height` reflects right-port count; branch-count change recomputes anchors + height (and re-centers existing ports).
- `ConnectionGeometry`: right-source pulls +X (back-compat curve unchanged), bottom-source pulls +Y, left-target pulls −X; `pull` uses distance-based min.
- `ConnectionViewModel`: `PathData` recomputes when an endpoint's `Height` changes.
- `MarqueeSelection`: selects a tall (grown) node whose lower half overlaps the marquee.
- Update existing `NodeLayoutTests` / `ConnectionGeometryTests` / `NodeViewModelPortsTests` / `ConnectionViewModelTests` to the new math/signatures.
- The `MainWindow.xaml` height binding has no unit test → covered by the user's visual verify.

## 6. Out of scope

- Orthogonal/elbow routing, port re-ordering, manual port placement.
- A `Top` edge (unused; only Left/Right/Bottom).
- Declaring port edges in AdbCore's `PortDefinition` (kept as an editor-layer name classification to avoid engine churn; can migrate later if more roles appear).

## 7. Merge handling

Logic is fully unit-tested in BotBuilder.Core.Tests, but the payoff (and the `MainWindow.xaml` height binding) is **visual** → opened as a PR and **user-verified + merged**, not self-merged. Independent of open PR #36 (`AdbCore/Execution` + action bases) — no shared files.
