# Tidy Up connection polish — Plan

> Extends the parked PR #81 branch `feat/editor-usability-visual`. Three connection-rendering refinements identified from real bot graphs: (A) clean vertical serpentine turns, (B) visually-distinct back-edges, (C) fewer branch-fan-out crossings.

**Goal:** Make Tidy Up's connections read cleanly — no sideways "balloon" on band turns, loops that obviously look like loops, and fewer branch crossings.

**Key constraint:** NO new `.bot` persistence. Port orientation is *derived display state* recomputed alongside `RerouteBackEdges` (on load and after every edit/move), exactly like back-route lanes. `portsFlipped` (already persisted) remains the band-level default; the orientation pass only overrides individual single-connection ports.

---

## A — Clean vertical turns (purist) + tightened curves (baseline)

**A1 (baseline, pure/TDD).** `ConnectionGeometry.ControlPoints` currently pulls each control point along the port's outward normal by `max(MinPull, dist/2)` where `dist` is the full endpoint distance. For a near-vertical edge on horizontal (Left/Right) ports this balloons sideways. Fix: scale each endpoint's pull by the distance *along that endpoint's own axis* — horizontal edges (Left/Right) use `|dx|`, vertical edges (Top/Bottom) use `|dy|` — so a stacked (near-vertical) connector on horizontal ports stays tight, and a Top/Bottom turn stays tall. Keep `MinPull` floor. Add tests: horizontal edge with large dy/small dx → small horizontal control offset; pure horizontal edge unchanged.

**A2 (Top edge).** Add `PortEdge.Top`; `NodeLayout.Outward(Top) => (0,-1)`; `NodeLayout.TopAnchor(index,count,width)` mirroring `BottomAnchor` (x distributed across the top edge, y=0). `ConnectionGeometry.IsBackward` for `Top`/`Bottom` sources: vertical ports never count as "backward" horizontally — return false (they route vertically; back-edge detection stays about horizontal returns). Add tests.

**A3 (derived orientation pass).** New `NodeViewModel` capability: reposition a single input or single output port to a chosen edge (reuse `PortViewModel.Reposition` + the anchor helpers). New method on `BotEditorViewModel`, `OrientSingleConnectionPorts()`, called from `AfterEdit()` and `DocumentMapper.Populate` (right before/with `RerouteBackEdges`). Algorithm, per connection that is the **sole** connection on BOTH its source output port and its target input port (i.e. 1-out→1-in, not fan-out/fan-in), and where neither endpoint is a failure/Bottom-designated port:
  - Determine dominant direction from source anchor to target anchor.
  - If target is clearly below (dy > 0 and |dy| > |dx|): source output → **Bottom**, target input → **Top**.
  - If clearly above: source → Top, target → Bottom.
  - If to the right: source → Right, target → Left. If to the left: source → Left, target → Right.
  - Re-anchor moved ports.
  Ports that are NOT sole-1-1 (branch outputs, join inputs, failure ports) are reset to their band default (Left/Right per `PortsFlipped`; failure stays Bottom) so the pass is fully idempotent and self-healing after drags. Recompute is on commit/load only (not mid-drag).
  This must compose with `portsFlipped`: the pass runs after flip and overrides only the sole-1-1 ports.

**Acceptance:** a 12-node serpentine chain (all sole-1-1) has its band-turn connectors as vertical drops (source port on Bottom, target port on Top); existing `AutoLayoutTests`, `NodeViewModelFlipTests`, `ConnectionGeometryTests`, `BackRoutePlannerTests` stay green (not weakened).

## B — Distinct back-edges

- Add derived `bool IsBackEdge` to `ConnectionViewModel` — true when the connection currently routes as a backward/return wire (`_lane is not null && ConnectionGeometry.IsBackward(start, SourcePort.Edge, end, Source.PortsFlipped)`). Raise its `PropertyChanged` wherever `PathData` changes (`SetLane`, `OnEndpointMoved`).
- In `MainWindow.xaml` connection template, the visible `Path` gets `StrokeDashArray` bound to `IsBackEdge` (dashed, e.g. `4 3`, when true; unset when false) via a small converter, and its `Stroke` becomes a `MultiBinding` over `IsSelected` + `IsBackEdge`: selected → selection colour; else back-edge → a muted "return wire" tint (theme brush, e.g. `SecondaryTextBrush`); else the normal stroke. Keep the transparent hit-test path unchanged.
- Add a theme-appropriate brush if needed; do not hardcode a colour that breaks in light theme.

**Acceptance:** loop-back/return wires render dashed + muted; forward wires unchanged; selection still visibly overrides.

## C — Fewer branch crossings

- Verify/strengthen `AutoLayout` sibling ordering so a branch's children are ordered by feeding-port Y (already partly present via the port-aware barycenter) — add a focused test: a 2-output node whose two children are placed so the wires don't cross (upper port → upper child).
- The A1 tightening + A3 orientation already reduce fan-out balloon; no separate routing rewrite. Keep C minimal — ordering correctness + a test.

---

## Process
- TDD for A1, A2, B (converter/flag logic where unit-testable), C (layout ordering). A3 orientation logic gets unit tests at the `BotEditorViewModel`/`NodeViewModel` level (port edges after a pass on a small graph). XAML is build-verified; visual result is the user's review in PR #81.
- Commit per enhancement with the `Claude-Session:` trailer. Do NOT push (controller reviews then updates the PR). Full `dotnet build`/`dotnet test` green before finishing.
