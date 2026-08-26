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
