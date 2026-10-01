# Feature Specification: Ribbon Menu Redesign

**Feature Branch**: `041-ribbon-menu-redesign`

**Created**: 2026-08-26

**Status**: Draft

**Input**: Replace the current two-row box-style expandable menu with a single-row ribbon-style layout for all circular workspace-shell controls (except Account). See `docs/RIBBON_MENU.md` for visual reference.

## Summary

Every `CircularAction` control that uses `ExpandableActionGroup` (i.e., all controls except the Account/list menu) must expand into a **horizontal or vertical ribbon** — a single rounded-rect pill containing all option icons in one row or column — instead of the current two-row box (trigger header + options row below). Direction of expansion is determined by the placement of the trigger button on screen.

## Requirements

### Visual Design

- Ribbon layout: single rounded-rectangle pill, all option icons in one line.
- Active/selected option highlighted with `#9C62DE` (purple circle background, white icon).
- Main trigger button collapsed color: `#45454D`.
- Main trigger button expanded/active color: `#2E7F26` (green).
- Pill background: retain existing dark glass (`oklch(0.18 0.02 280 / 0.97)`) — theme-independent.

### Expansion Direction

| Trigger placement | Ribbon expands |
|---|---|
| `right-stack` (right edge) | Left (horizontal) |
| `top-cluster` (top edge) | Down (vertical) |
| `bottom-end` (bottom edge) | Up (vertical) |
| Left edge (future) | Right (horizontal) |

### Trigger Overlap and Pin (amendment, 2026-10-01)

- The pill's trigger-side edge sits 10 px inside the trigger Fab's outer edge, so the rounded cap tucks under the Fab in every direction.
- A `pill` ribbon shows a 24 px pin badge on its far tip, hanging 8 px outside it. While pinned, the ribbon ignores outside clicks and stays open when another control is expanded; Escape is ignored while pinned; the trigger unpins and closes it. The pin slides with the ribbon's reveal.
- Pin colours: unpinned = surface background (`#fefefe` / `#11121c`), ribbon border, red pin (`red` / `#c34e4e`); pinned = colours swapped. Session-only state (`workspaceOverlayStore.pinnedControlIds`).
- `card` shapes (Account menu) have no pin.

### Exceptions

- The **Account menu** (`layout="list"`) is excluded — it retains its existing icon+label list layout and dropdown-down expansion. No changes required.

## Affected Files

- `src/AskLucy.Web/ClientApp/src/components/workspace-shell/CircularAction.tsx`
- `src/AskLucy.Web/ClientApp/src/components/workspace-shell/ExpandableActionGroup.tsx`
- `src/AskLucy.Web/ClientApp/src/components/workspace-shell/WorkspaceOverlay.tsx`
- `src/AskLucy.Web/ClientApp/src/components/workspace-shell/types.ts`
- `src/AskLucy.Web/ClientApp/src/components/workspace-shell/FloatingPanel.tsx` (reads `selectIsControlExpanded`)
- `src/AskLucy.Web/ClientApp/src/store/workspaceOverlayStore.ts`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Right-side button expands into horizontal ribbon (Priority: P1)

A user clicks a right-edge circular button (e.g. Layers). The ribbon appears as a horizontal pill to the left of the trigger. The trigger turns green. The active option is highlighted purple. Clicking away or pressing Escape collapses back to the dark-gray circle.

**Acceptance Scenarios**:

1. **Given** the Layers button is collapsed, **When** the user clicks it, **Then** a horizontal ribbon pill appears to its left containing all layer option icons.
2. **Given** the ribbon is open, **Then** the trigger Fab background is `#2E7F26` (green).
3. **Given** an option in the ribbon is the active/selected one, **Then** it renders with a `#9C62DE` purple background and white icon.
4. **Given** the ribbon is open, **When** the user clicks away, **Then** the ribbon collapses and the trigger returns to `#45454D`.
5. **Given** the ribbon is open, **When** the user presses Escape, **Then** the ribbon collapses and focus returns to the trigger.

### User Story 2 — Top button expands downward (Priority: P1)

A user clicks a top-cluster button (e.g. Account is excluded; any future top-placed action-group control). The ribbon appears below the trigger.

**Acceptance Scenarios**:

1. **Given** a top-cluster `action-group` control, **When** expanded, **Then** the ribbon pill appears below the trigger.

### User Story 3 — Bottom button expands upward (Priority: P2)

A user clicks the bottom-end button. The ribbon appears above the trigger.

**Acceptance Scenarios**:

1. **Given** a `bottom-end` control, **When** expanded, **Then** the ribbon pill appears above the trigger.

### User Story 4 — Account menu unchanged (Priority: P1)

The Account button (top-cluster, `layout="list"`) continues to work exactly as before — icon+label list, no ribbon.

**Acceptance Scenarios**:

1. **Given** the Account button is clicked, **Then** the existing list-style panel appears below it, unchanged.

### User Story 5 — Pin a ribbon open (Priority: P2)

A user pins a ribbon so it stays open while they work with the map.

**Acceptance Scenarios**:

1. **Given** an open ribbon, **When** the user clicks its pin, **Then** the badge turns solid red and the ribbon stays open on outside clicks and when another control is expanded.
2. **Given** a pinned ribbon, **When** the user clicks its trigger, **Then** it unpins and closes; **When** the user presses Escape, **Then** nothing happens.
3. **Given** a pinned ribbon, **When** the user clicks the pin again, **Then** it unpins and stays open as the normal (click-away) ribbon.
4. **Given** the Account menu (card shape), **Then** no pin is rendered.
