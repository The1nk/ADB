# Layouts Reference

## Contents
- Layout Philosophy
- Main Window Structure
- Panel / Splitter Patterns
- Canvas Viewport
- Dialog Layout
- DPI Awareness

---

## Layout Philosophy

ADB UIs are **tooling surfaces** — optimize for density and discoverability, not whitespace. Use fixed-width side panels with a dominant central canvas. Avoid fluid/responsive grid thinking; this is a desktop app with a resizable window, not a web page.

---

## Main Window Structure (BotBuilder)

```
┌─────────────────────────────────────────────────────┐
│ Menu / Toolbar                                      │
├──────────┬──────────────────────────┬───────────────┤
│ Palette  │     Canvas (scrollable)  │  Properties   │
│ (fixed)  │                          │  (fixed)      │
├──────────┴──────────────────────────┴───────────────┤
│ Target Bar / Status                                  │
└─────────────────────────────────────────────────────┘
```

Use `GridSplitter` between palette/canvas/properties columns. Set minimum widths to prevent panels collapsing entirely.

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="180" MinWidth="120" />
        <ColumnDefinition Width="5" />   <!-- splitter -->
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="5" />   <!-- splitter -->
        <ColumnDefinition Width="240" MinWidth="160" />
    </Grid.ColumnDefinitions>
</Grid>
```

---

## Panel and Splitter Patterns

```xml
<!-- GOOD — draggable splitter with theme-aware background -->
<GridSplitter Grid.Column="1"
              Width="5"
              HorizontalAlignment="Stretch"
              Background="{DynamicResource BorderBrush}"
              ResizeBehavior="PreviousAndNext" />
```

AVOID using `StackPanel` inside a `ScrollViewer` for long lists — use `VirtualizingStackPanel` (default for `ListBox`/`ItemsControl` with virtualization enabled) to avoid layout performance issues with large action lists.

---

## Canvas Viewport (BotBuilder)

The node canvas supports pan, zoom, and selection. Layout rules:
- Pan: `TranslateTransform` on the canvas content
- Zoom: `ScaleTransform` centered on cursor position
- Zoom % display and Reset (Ctrl+0) / Fit-to-nodes (Ctrl+Shift+0) are required chrome (PR #49)

```xml
<!-- Canvas transform group — existing pattern -->
<Canvas>
    <Canvas.RenderTransform>
        <TransformGroup>
            <ScaleTransform x:Name="CanvasScale" />
            <TranslateTransform x:Name="CanvasTranslate" />
        </TransformGroup>
    </Canvas.RenderTransform>
</Canvas>
```

---

## Dialog Layout

Dialogs should be task-focused and compact. Use a vertical `StackPanel` or `Grid` with a standard button row at bottom.

```xml
<Grid Margin="12">
    <Grid.RowDefinitions>
        <RowDefinition Height="*" />        <!-- content -->
        <RowDefinition Height="Auto" />     <!-- button row -->
    </Grid.RowDefinitions>
    <!-- content -->
    <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,8,0,0">
        <Button Content="OK" IsDefault="True" Width="80" Margin="0,0,8,0" />
        <Button Content="Cancel" IsCancel="True" Width="80" />
    </StackPanel>
</Grid>
```

Add a scroll cap to tall dialogs (e.g., PreviewConfirmView) to prevent overflow on small screens:
```xml
<ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="480">
    <!-- content -->
</ScrollViewer>
```

---

## DPI Awareness

This app declares Per-Monitor V2 DPI awareness. Never hardcode pixel sizes for interactive hit targets or icon sizes.

```xml
<!-- BAD — breaks on 150% / 200% DPI displays -->
<Button Width="16" Height="16" />

<!-- GOOD — use em-relative or ViewBox-scaled content -->
<Button Padding="4">
    <Viewbox Width="16" Height="16"><Path .../></Viewbox>
</Button>
```