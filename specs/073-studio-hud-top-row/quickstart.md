# Quickstart: Validate the Studio HUD Top Row

**Feature**: [spec.md](spec.md) | **Contracts**: [studio-hud-row.md](contracts/studio-hud-row.md), [extension-context-hud-item.md](contracts/extension-context-hud-item.md)

## Prerequisites

- Frontend dependencies installed in `src/AskLucy.Web/ClientApp`.
- For the live check: the app is running (`https://localhost:7170`), you are signed in, and at least one AI provider can resolve a site.

## 1. Automated checks

Run these from `src/AskLucy.Web/ClientApp`:

```bash
npx tsc -b --noEmit          # NOT bare `tsc --noEmit` — the root tsconfig uses project references
npx eslint src
npx vitest run               # FULL suite — ChatPage.test.tsx asserts independently of component tests
```

Expected: all three pass. Tests that must exist and pass:

| Area | Proves |
|---|---|
| `WorkspaceOverlay.test.tsx` | W1–W6: `topStart` renders in a start group carrying `RESERVED_ATTRIBUTE`; absent `topStart` changes nothing |
| `FloatingToolbar.test.tsx` | `placement="inline"` drops absolute positioning; the default is unchanged |
| `HudCard.test.tsx` | H1–H3: 40 px, theme surface, no stripe |
| `SiteBoundaryConfidenceBadge.test.tsx` | B1–B5: shield + bold name only, no label/divider/alternatives on the card; the shield's tooltip shows the level and its reason; the shield takes the pointer and is in the tab order; correct icon and tone per level, accessible name |
| `SiteBoundaryConfidenceBadge.a11y.test.tsx` | axe passes for each level, in both themes |
| `LocationWeatherWidget.test.tsx` / `.a11y` | the stale marker is inline (no third line); the accessible name still says "(last known reading)" |
| `context.test.ts` / `extensibility.test.tsx` | X1–X5: `hudItem` recorded, rendered, withdrawn on stop; hosts ignore each other's kinds |
| `ContributionErrorBoundary.test.tsx` | X7: a throwing item marks its extension failed and renders nothing; sibling items and the surrounding tree survive |
| `useAvoidReservedCorner.test.ts` | a left-corner widget sits just below a reserved start-group row |
| `SiteBoundaryConfidenceBadge.test.tsx` (contrast) | each tone's measured contrast against `background.paper` is ≥ 3:1 in both themes (FR-011; axe does not check non-text contrast) |
| `ChatPage.test.tsx` | row order: Home → title → weather → site card |

## 2. Live visual check (screenshot loop)

Take one screenshot after each step. Claude judges the screenshots against the expected values.

1. Open `/studio` in **dark** theme at a 1440 px-wide window. Wait for the weather to load.
   **Expect**: Home ● → [Flumeria Studio] → [weather] on one line at the top-left, all 40 px tall and centred with the top-right buttons. Nothing is stacked below.
2. In chat, send: `Show me Al Safa Park 2`. Wait for the boundary to draw.
   **Expect**: a 4th card appears to the right of the weather as soon as Lucy confirms the place: a **green** shield with a check, then **Al Safa Park 2** in bold. No other text is on the card. Hovering the shield shows "High confidence" and the reason ("The map service matched this exact spot.", then "Outline: OpenStreetMap (…)" once the boundary draws). The first three items haven't moved.
3. Switch to **light** theme.
   **Expect**: all four items switch surface together and stay identical to each other. The shield is still green and clearly visible.
4. Resize the window to 800 px wide, then to 390 px.
   **Expect**: row items wrap onto a second line under the first. Nothing overlaps the top-right buttons or runs off-screen.
5. Ask for a site that resolves with Medium or Low confidence (for example, a vague name like `Show me the park near Safa`).
   **Expect**: an **amber** plain shield (tooltip "Medium confidence") or a **red** shield with `!` (tooltip "Low confidence — approximate").
6. Turn on Solar Analysis so the camera-attitude widget appears on the left.
   **Expect**: the widget sits directly under the row, about 8 px below it. There's no leftover gap where the old stacked cards used to be.
7. Turn off the `viewer.boundary-confidence` extension (or stop it from the extension panel, if one is exposed).
   **Expect**: the site card disappears, and the other three items don't move.

## 3. Accessibility spot check

With a screen reader, or DevTools' accessibility tree, on step 2's state: the site card announces "Al Safa Park 2 boundary: High confidence" ("location" in place of "boundary" before the outline lands), Tab reaches the shield and opens its tooltip, and the weather card announces "Weather in …: NN°C, …".
