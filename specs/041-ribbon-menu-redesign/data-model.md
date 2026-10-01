# Data Model: Ribbon Menu Redesign

No new domain entities, database tables, or API contracts. This is a pure frontend visual redesign.

## Type Changes

### `ExpandDirection` (new type, `CircularAction.tsx`)

```ts
export type ExpandDirection = 'left' | 'right' | 'up' | 'down'
```

Added to `CircularActionProps`:
```ts
expandDirection?: ExpandDirection  // default: 'down' (backward-compatible)
```

### `ControlPlacement` → `ExpandDirection` mapping (in `WorkspaceOverlay.tsx`)

```ts
const PLACEMENT_DIRECTION: Record<ControlPlacement, ExpandDirection> = {
  'top-cluster': 'down',
  'right-stack': 'left',
  'bottom-end': 'up',
}
```

No changes to `ControlDefinition`, `ControlPlacement`, `ControlKind`, or `ControlStatus` types.

## Color Token Changes

`CIRCULAR_ACTION_CHROME` in `CircularAction.tsx`:

| Token | Before | After |
|---|---|---|
| `collapsedBg` | `oklch(0.25 0.02 280 / 0.85)` | `#45454D` |
| Fab bg when expanded | (transparent — inherited from outer Box) | `#2E7F26` |

`ExpandableActionGroup.tsx` — highlighted action button:

| Property | Before | After |
|---|---|---|
| `bgcolor` (highlighted) | `warning.main` (amber) | `#9C62DE` |
| `color` (highlighted) | `#1C1B18` (dark) | `#fff` |
| `bgcolor` hover (highlighted) | `warning.dark` | `#7B43C0` |

## Amendment (2026-10-01): trigger overlap and pin

- **Overlap** — a pill's Fab-side edge sits `TRIGGER_OVERLAP_PX` (10 px) inside the Fab's outer edge in all four directions (`right`/`left`/`bottom`/`top: 10px`), so the rounded cap tucks under the Fab. The Fab-side padding shrinks by the same 10 px (48 → 38) to keep the 8 px gap to the nearest option.
- **Pin** — `CircularAction` takes `pinned` / `onTogglePin`. When `onTogglePin` is set (pill shape only) a 24 px badge renders on the ribbon's far tip, hanging `PIN_OVERHANG_PX` (8 px) outside it in every direction. It is a sibling of the clipped content box (the `clip-path` would otherwise cut off the overhang) and rides the reveal: it animates from the trigger end to the far tip over the same 220 ms curve as the clip-path. Last in DOM order (Tab: Fab → options → pin). Flat, no shadow: unpinned = surface background (`#fefefe` / `#11121c`) with the ribbon's border and a red pin (`red` / `#c34e4e`) tilted 45°; pinned = colours swapped, upright filled pin.
- **State** — `workspaceOverlayStore.pinnedControlIds`. A pinned ribbon is expanded independently of `expandedControlId`: outside clicks, `collapse()` and expanding another control leave it open. Its own trigger unpins and closes it; Escape is ignored while pinned. `selectIsControlExpanded(id)` is the one "is it open" check. Pins are session-only, like the rest of the store.
