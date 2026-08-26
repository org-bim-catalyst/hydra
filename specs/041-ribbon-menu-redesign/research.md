# Research: Ribbon Menu Redesign

## Decision 1: Collapse orientation for horizontal ribbons

**Decision**: For `'left'` and `'right'` expansion directions, switch MUI `Collapse` from default `orientation="vertical"` (animates height) to `orientation="horizontal"` (animates width). For `'up'` and `'down'`, keep vertical Collapse.

**Rationale**: MUI's `Collapse` supports an `orientation` prop since MUI v5. Horizontal Collapse smoothly animates the width from 0 → content width, which is exactly the ribbon unfurling effect needed for left/right expansions. No third-party animation library required.

**Alternatives considered**: CSS `max-width` transition — requires knowing the content's max width in advance (or using `max-width: 999px` as a proxy), which produces a non-linear ease mismatch. MUI Collapse is preferable because it integrates with MUI's theme transition curves.

---

## Decision 2: flexDirection for directional expansion

**Decision**: The outer `Box` in `CircularAction` uses `flexDirection` to control which side the ribbon appears relative to the trigger:

| Prop value | flexDirection | Result |
|---|---|---|
| `'left'` | `row-reverse` | Trigger right, content left |
| `'right'` | `row` | Trigger left, content right |
| `'up'` | `column-reverse` | Trigger bottom, content top |
| `'down'` | `column` | Trigger top, content bottom (existing) |

**Rationale**: Flex order is the most straightforward way to reorder DOM elements visually without changing the DOM order (keyboard navigation stays correct).

**Alternatives considered**: CSS `order`, absolute positioning — both add complexity or break natural DOM flow.

---

## Decision 3: Width-zero hack for vertical Collapse / height-zero for horizontal

**Decision**: The existing `width: expanded ? 'auto' : 0` hack on the inner content `Box` (vertical Collapse) is kept as-is. For horizontal Collapse, the equivalent `height: expanded ? 'auto' : 0` is added to prevent the collapsed content's natural height from stretching the pill taller than the trigger circle.

**Rationale**: MUI Collapse animates one dimension but leaves the other at its natural size. Without this guard, a collapsed horizontal ribbon with 5 action icons would still occupy icon-height space vertically, making the trigger `Box` taller than a circle.

---

## Decision 4: Trigger Fab color — per-state explicit background

**Decision**: Give the Fab its own `bgcolor` that changes per state:
- Collapsed: `#45454D`
- Expanded: `#2E7F26`

The outer Box background changes to the dark glass only when expanded (to form the ribbon pill behind the content); the Fab itself becomes transparent so the glass shows through for the trigger area of an open ribbon too — but the initial click changes the Fab to green visually before the Collapse finishes.

**Rationale**: The outer Box's single background can't simultaneously be `#45454D` (for the trigger) and dark glass (for the pill). Giving the Fab an explicit background decouples the two zones.

---

## Decision 5: Highlighted action color

**Decision**: Replace `warning.main` (amber) highlight in `ExpandableActionGroup` with `#9C62DE` (purple) per the design spec. Icon color on highlighted button: `#fff` (white). Hover state: `#7B43C0` (a ~20% darkened variant).

**Rationale**: Amber was from the readdy.ai reference for the Analysis "run" action specifically. The new design specifies purple `#9C62DE` as the universal active-option highlight across all ribbons.

---

## Decision 6: Content padding direction

**Decision**: Content `Box` padding is adjusted for horizontal ribbons:
- Vertical (`down`/`up`): existing `px: 1.5, pb: 1.25, pt: 0.5`
- Horizontal (`left`/`right`): `py: 1.25, pl: 1.25, pr: 0.5` (for `left`); mirror for `right`

**Rationale**: Padding needs to be on the sides of the content that face away from the trigger to maintain visual balance in the pill.

---

## Decision 7: Account menu exclusion

**Decision**: No changes to the Account control. It uses `layout="list"` in `ExpandableActionGroup` and is in `top-cluster` placement, where `expandDirection` will be `'down'` — which is the existing behavior. The list layout's styling (amber etc.) is unaffected since it uses `ListItemButton`, not the `highlighted` icon button path.

**Rationale**: The spec explicitly excludes the Account menu. The Account control happens to use the `'down'` direction already, so passing `expandDirection` to it is harmless — it only affects the ribbon layout path (`layout !== 'list'`).
