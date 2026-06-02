# Canvas Double-Click Drop Position Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Double-clicking a toolbox item should drop the new node at the center of the *currently visible* canvas viewport (honoring pan/zoom), not at a fixed point in world space.

**Architecture:** The canvas maps world→screen as `screen = world * Scale + Offset` (`CanvasViewport`). The drag-drop path already works because WPF's `e.GetPosition(NodeHost)` returns world coordinates (the node host lives inside the transformed grid, so the inverse transform is applied automatically). The double-click path is the only broken one: it uses `NodeHost.ActualWidth/2` — the world-space center of the canvas content, a fixed point independent of pan/zoom. Fix: add a pure `ScreenToWorld` inverse-transform method on `CanvasViewport` and feed it the center of the visible viewport (`ViewportHost`) in screen space.

**Tech Stack:** C# / .NET 10, WPF, CommunityToolkit.Mvvm, xUnit. Projects: `BotBuilder.Core` (WPF-free view-models — where the testable math lives), `BotBuilder` (thin WPF shell), `BotBuilder.Core.Tests`.

---

## File Structure

- `BotBuilder.Core/Canvas/CanvasViewport.cs` — add `ScreenToWorld(double, double)` (pure inverse transform). The math mirrors the `worldX = (anchor - Offset) / Scale` already inside `ZoomAt`.
- `BotBuilder.Core.Tests/CanvasViewportTests.cs` — unit-test `ScreenToWorld` (identity, panned, zoomed, round-trip).
- `BotBuilder/MainWindow.xaml.cs` — the double-click handler `PaletteItem_MouseLeftButtonDown` uses `ScreenToWorld` with the visible viewport's center.

---

## Task 1: Add `ScreenToWorld` to `CanvasViewport`

Add the pure inverse-transform helper. `CanvasViewport` is view-framework-free, so it returns a `(double X, double Y)` tuple rather than a WPF `Point`.

**Files:**
- Modify: `BotBuilder.Core/Canvas/CanvasViewport.cs`
- Test: `BotBuilder.Core.Tests/CanvasViewportTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `CanvasViewportTests.cs`:

```csharp
    [Fact]
    public void ScreenToWorld_Identity_ReturnsSamePoint()
    {
        var v = new CanvasViewport();

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(100, x, 6);
        Assert.Equal(50, y, 6);
    }

    [Fact]
    public void ScreenToWorld_WithPan_SubtractsOffset()
    {
        var v = new CanvasViewport();
        v.Pan(30, -20);

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(70, x, 6);
        Assert.Equal(70, y, 6);
    }

    [Fact]
    public void ScreenToWorld_WithZoom_DividesByScale()
    {
        var v = new CanvasViewport();
        v.ZoomAt(0, 0, 2.0); // Scale = 2, Offset stays 0

        var (x, y) = v.ScreenToWorld(100, 50);

        Assert.Equal(50, x, 6);
        Assert.Equal(25, y, 6);
    }

    [Fact]
    public void ScreenToWorld_IsInverseOfWorldToScreenTransform()
    {
        var v = new CanvasViewport();
        v.Pan(40, 15);
        v.ZoomAt(200, 120, 1.5);

        const double worldX = 333, worldY = -77;
        var screenX = worldX * v.Scale + v.OffsetX;
        var screenY = worldY * v.Scale + v.OffsetY;

        var (x, y) = v.ScreenToWorld(screenX, screenY);

        Assert.Equal(worldX, x, 6);
        Assert.Equal(worldY, y, 6);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ScreenToWorld"`
Expected: FAIL to compile (`ScreenToWorld` does not exist).

- [ ] **Step 3: Implement `ScreenToWorld`**

In `CanvasViewport.cs`, add after `ZoomAt`:

```csharp
    /// <summary>Converts a screen-space point to world space — the inverse of
    /// <c>screen = world * Scale + Offset</c>. Use it to place content at a screen location
    /// (e.g. dropping a node at the center of the visible viewport) regardless of pan/zoom.</summary>
    public (double X, double Y) ScreenToWorld(double screenX, double screenY)
        => ((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test BotBuilder.Core.Tests/BotBuilder.Core.Tests.csproj --filter "FullyQualifiedName~ScreenToWorld"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add BotBuilder.Core/Canvas/CanvasViewport.cs BotBuilder.Core.Tests/CanvasViewportTests.cs
git commit -m "feat(canvas): add CanvasViewport.ScreenToWorld inverse transform"
```

---

## Task 2: Drop double-clicked nodes at the visible viewport center

Point the toolbox double-click handler at `ScreenToWorld`, fed by the center of the visible viewport (`ViewportHost`) in screen space. This is a WPF-shell change with no unit test (it's view glue); the math it relies on is covered by Task 1.

**Files:**
- Modify: `BotBuilder/MainWindow.xaml.cs` (the `PaletteItem_MouseLeftButtonDown` handler)

- [ ] **Step 1: Update the double-click handler**

In `MainWindow.xaml.cs`, the current handler is:

```csharp
    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _paletteMouseDownPoint = e.GetPosition(this);

        if (e.ClickCount == 2 && PaletteItemFrom(sender) is { } item)
        {
            var centre = new Point(NodeHost.ActualWidth / 2, NodeHost.ActualHeight / 2);
            _editor.AddNode(item.TypeKey, centre.X, centre.Y);
        }
    }
```

Replace the body of the `if` block so it drops at the center of the visible viewport, converted to world coordinates:

```csharp
    private void PaletteItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _paletteMouseDownPoint = e.GetPosition(this);

        if (e.ClickCount == 2 && PaletteItemFrom(sender) is { } item)
        {
            // Drop at the center of the *visible* viewport, mapped back to world space, so the node
            // lands where the user is looking regardless of pan/zoom (ViewportHost is the on-screen
            // viewport; NodeHost lives inside the pan/zoom transform).
            var (worldX, worldY) = _editor.Viewport.ScreenToWorld(ViewportHost.ActualWidth / 2, ViewportHost.ActualHeight / 2);
            _editor.AddNode(item.TypeKey, worldX, worldY);
        }
    }
```

Note: if the `System.Windows.Point` import is now unused elsewhere in the file, leave the `using` as-is only if other code uses `Point` (it does — drag/pan/marquee fields are `Point`). Do not remove still-used usings.

- [ ] **Step 2: Build the WPF project**

Run: `dotnet build BotBuilder/BotBuilder.csproj`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add BotBuilder/MainWindow.xaml.cs
git commit -m "fix(builder): drop double-clicked toolbox items at visible viewport center"
```

---

## Final verification

- [ ] Run the full suite: `dotnet test ADB.slnx` — expected all green (prior count + 4 new `ScreenToWorld` tests).
- [ ] Hands-on (user): pan and/or zoom the canvas, then double-click a toolbox item — the node should appear in the middle of the visible area, not drift to a fixed canvas location. (Drag-drop already behaved correctly and should be unchanged.)
