---
name: frontend-design
description: |
  Applies WPF XAML styling, Light/Dark/HighContrast theming, and design consistency for the ADB desktop toolkit.
  Use when: modifying BotBuilder or BotCapture UI, adding new controls, wiring theme brushes, adjusting layouts, or ensuring new surfaces match established visual language.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash
---

# Frontend Design Skill

ADB is a dense, functional Windows desktop tool — BotBuilder is a node-graph editor, BotCapture a screenshot utility. The UI should feel like a professional developer tool: quiet, compact, scannable. Avoid decorative chrome, gradients, or generic "modern app" styling. Every new control must participate in the Light/Dark/HighContrast theme system via `AdbUi.Theme` brushes.

## Before You Code (REQUIRED)

This skill's content was captured at generation time and MAY be stale. For ANY non-trivial change involving frontend-design, verify against current docs FIRST:



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

## Design Direction

Pick a clear visual direction from the product context before writing styles. The direction must fit the target surface (unknown) and the repo's actual UI vocabulary, not a generic AI-looking template. A SaaS dashboard should usually feel dense, quiet, and fast to scan; a marketing page can be more memorable; an Ink/CLI view should prioritize stable layout, truncation, and keyboard clarity.

## Interface Quality Bar

- Make the interface feel intentionally designed for this repo, not assembled from interchangeable cards and gradients.
- Use distinctive choices only when they serve the surface; restraint is a design choice when the product is operational or data-heavy.
- Avoid AI slop: random purple/blue gradients, unrelated glass panels, oversized hero typography inside tools, fake depth, decorative noise with no product meaning, and one-note color palettes.
- Keep text, icons, states, and layout aligned with the user's actual workflow.

## Component System Fit

- Styling system: the styling system discovered in this repo.
- Component primitives/libraries: the existing component primitives.
- Real UI surfaces to inspect first: the nearest real screen, component, or interactive surface.
- Reuse local tokens, spacing, border radii, density, icons, and interaction patterns before creating new primitives.

## Responsive + State Coverage

- Cover loading, empty, error, disabled, pending, success, and recovery.
- Keep layouts stable for long labels, long data values, narrow mobile widths, and wide desktop.
- For interactive elements, verify labels, focus states, keyboard flow, semantics, and contrast.

## Visual Anti-Patterns

- Do not copy a generic design-skill template or repeat the same aesthetic across projects.
- Do not introduce a new brand palette, font stack, shadow system, animation language, or card style unless it matches repo evidence or the user explicitly asks.
- Do not make dashboards look like landing pages, or landing pages look like admin tables.

## Verification Checklist

- Nearby components/screens were inspected.
- Visual direction matches the target surface and product context.
- Responsive behavior, overflow, empty/loading/error/disabled states, and accessibility basics were checked.
- Any generated example uses real repo files/symbols from the evidence pack or is labeled as new code to add.

## Quick Start

### Apply a Theme Brush (Existing Pattern)

```xml
<!-- Existing pattern — brush names live in AdbUi.Theme -->
<Border Background="{DynamicResource WindowBackgroundBrush}">
    <TextBlock Foreground="{DynamicResource PrimaryTextBrush}" Text="Label" />
</Border>
```

Use `DynamicResource` (not `StaticResource`) so the control reacts to runtime theme switches.

### Add a New Themed Control Style (New Code Pattern)

```xml
<!-- new code to add — in AdbUi.Theme resource dictionary -->
<Style TargetType="local:MyControl">
    <Setter Property="Background" Value="{DynamicResource PanelBackgroundBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource BorderBrush}" />
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="local:MyControl">
                <Border Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="1">
                    <ContentPresenter />
                </Border>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| DynamicResource | Theme-reactive brush binding | `{DynamicResource WindowBackgroundBrush}` |
| ControlTemplate | Full visual replacement for complex controls | ComboBox, Menu, ListBox require templates |
| ThemeManager | Apply/switch themes at runtime | `ThemeManager.Apply(ThemeKind.Dark)` |
| PerMonitorV2 | DPI-aware layout | Never hardcode pixel sizes |
| ToString() on VMs | ComboBox selection box display | Required when not using DisplayMemberPath |

## Common Patterns

### ComboBox / ListBox — Always Template

WPF ComboBox popup AND selection box must both be templated; setters alone do not theme the dropdown. See `references/components.md`.

### Menu / MenuItem — Always Template

Setters alone don't theme WPF menus. A full `ControlTemplate` is required. Confirmed by PR #44.

### Disabled Controls (Palette Greying)

Soft-grey disabled state is applied via `IDependencyProbe` — do not set `IsEnabled=False` without supplying a themed disabled brush. See `references/components.md`.

## Verification

```bash
bun run check-types   # if applicable
dotnet build ADB.slnx # must produce zero warnings on XAML binding errors
```

## See Also

- [aesthetics](references/aesthetics.md)
- [components](references/components.md)
- [layouts](references/layouts.md)
- [motion](references/motion.md)
- [patterns](references/patterns.md)

## Related Skills

See the **wpf** skill for WPF-specific XAML patterns and data binding.
See the **dotnet** skill for .NET 10 project structure and build tooling.
See the **ux** skill for interaction design and accessibility guidance.