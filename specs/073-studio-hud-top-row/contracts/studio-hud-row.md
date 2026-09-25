# UI Contract: Studio HUD Row

**Feature**: [spec.md](../spec.md) | **Research**: [research.md](../research.md) D1, D2, D4–D6, D9

## `WorkspaceOverlay` — new `topStart` slot

```ts
interface WorkspaceOverlayProps {
  controls: ControlDefinition[]
  topClusterLeading?: ReactNode
  /** NEW — items laid out left-to-right in the top-left corner, sharing one row (and one
   * centreline) with the top-right cluster. Wraps inside the space the cluster leaves free. */
  topStart?: ReactNode
  children?: ReactNode
}
```

| # | Guarantee |
|---|---|
| W1 | When `topStart` or top-cluster content is present, one top bar is rendered: `position: absolute; top: 0; left: 0; right: 0`, with the margin `FloatingToolbar` uses today (`{xs: 16, sm: 24}`). It is `display: flex`, `alignItems: flex-start`, `pointerEvents: none`. |
| W2 | Start group: `flex: 0 1 auto; minWidth: 0; flexWrap: wrap; alignItems: center; gap: 8px`. It carries `RESERVED_ATTRIBUTE` on its own in-flow root. |
| W3 | Top-right cluster: the existing `FloatingToolbar` with `placement="inline"`, `ml: auto`, `flex: none`, and `RESERVED_ATTRIBUTE`. Its content, order, and wrap behaviour are unchanged. |
| W4 | The start group never overlaps the cluster at any viewport width from 360 to 2560 px (SC-005). |
| W5 | When `topStart` is absent, the rendered DOM is behaviourally identical to today (existing `WorkspaceOverlay.test.tsx` passes unchanged). |
| W6 | The `right-stack` and `bottom-end` clusters are unchanged. |

## `FloatingToolbar` — new `placement` prop

```ts
placement?: 'anchored' | 'inline'   // default 'anchored' (today's behaviour)
```

With `'inline'`: no `position: absolute`, no anchor offsets, no outer margin, no `maxWidth` calc. `direction`, `flexWrap`, `alignItems`, `justifyContent`, spacing, and `dataAttributes` behave exactly as with `'anchored'`.

## `HudCard` — shared surface (new, `components/workspace-shell/HudCard.tsx`)

```ts
interface HudCardProps {
  children: ReactNode
  /** default 'none' — the card is decoration over the map (FR-013). */
  pointerEvents?: 'none' | 'auto'
  maxWidth?: number
  sx?: SxProps<Theme>
  /** passthrough for role/aria-label on status cards */
  role?: string
  'aria-label'?: string
}
```

| # | Guarantee |
|---|---|
| H1 | Height is exactly 40 px. Content is laid out in a row, vertically centred. |
| H2 | Surface: `CIRCULAR_ACTION_CHROME.expandedBg` / `.border` / `.icon`, `backdropFilter: blur(12px)`, `borderRadius: 2`, shadow `0 2px 10px rgba(0,0,0,0.28)`. It is theme-driven only, with no mode branches or hardcoded colours (FR-002a). |
| H3 | No accent stripe and no accent border. |

## Row composition (studio)

`ChatPage` passes `topStart` to `WorkspaceOverlay` in this order (FR-001):

1. Home button: the existing 40 px `Fab`, unchanged behaviour (FR-013).
2. Project title chip: a `HudCard` containing "Flumeria Studio".
3. `LocationWeatherWidget`: returns a `HudCard` or `null`.
4. `ExtensionHudItemHost`: renders every `hudItem` contribution. The `viewer.boundary-confidence` contribution returns a `HudCard` or `null`.

`HomeProjectCard` returns a fragment (Home button + title chip) so the two are separate row items. It no longer positions itself. `LocationWeatherWidget` is no longer a direct `ChatPage` child.

| # | Guarantee |
|---|---|
| R1 | An item that renders `null` leaves no gap, because flex `gap` only applies between rendered children (FR-003). |
| R2 | Items to the left of an item that appears, disappears, or resizes don't move (FR-004). This holds by construction, because the row is left-anchored. |
| R3 | No row item sets its own `position`, `top`, or `left`. |

## `LocationWeatherWidget` states (inside the 40 px card)

| State | Content |
|---|---|
| no location | `null` |
| loading, no prior reading | `null` |
| error, no prior reading | one line: "Weather unavailable" (reduced opacity), `role="status"` |
| reading | one line: condition icon · `NN°C` (`subtitle2`, weight 600) · vertical divider (`aria-hidden`) · location name (`body2`, ellipsis) — amended 2026-09-25 after the first live check, replacing the two-line name-over-temperature layout |
| stale reading | same as reading, with an inline `· last known` marker on line 2. The accessible name still ends "(last known reading)". |

## `SiteBoundaryConfidenceBadge` (inside the 40 px card)

| # | Guarantee |
|---|---|
| B1 | Renders `null` unless both `siteName` and `confidenceLevel` are set. |
| B2 | Shows exactly, on one line: a confidence icon · the confidence label (`subtitle2`, weight 600) · vertical divider (`aria-hidden`) · `siteName` (`body2`, `noWrap`, ellipsis). Nothing else (FR-006, FR-007). Amended 2026-09-25 after the live check, replacing the two-line name-over-label layout to match the weather card. |
| B3 | Icon and colour follow data-model.md "confidence → visual". Icons are `aria-hidden`. |
| B4 | `role="status"`, `aria-label="{siteName} boundary: {confidence label}"` (FR-012). |
| B5 | `maxWidth: 360` (was 260 while the card was two lines; one line needs the label and the name side by side). |
