# Phase 1 Data Model: Clear-Area Panel Placement & Reopen Tray

All state is client-side and session-scoped. **No database entity, no API payload, and no backend type changes.**

---

## Rect

A plain axis-aligned rectangle in `FloatingPanelHost`-relative coordinates (origin = the host's top-left, matching how `FloatingPanel.position` is already interpreted).

| Field | Type | Notes |
|---|---|---|
| `x` | `number` | Left edge, px. May be 0. |
| `y` | `number` | Top edge, px. May be 0. |
| `width` | `number` | > 0 |
| `height` | `number` | > 0 |

**Validation**: Rects with zero or negative width/height are discarded by `collectReservedRects` — a hidden or unmounted element measures as zero in the DOM and must not be treated as an obstacle at the origin.

**Location**: `viewer/panels/types/panel.ts` (exported, so the layout module and the store share one definition).

---

## Reserved Region

Not a stored entity — a *derived* value. Produced on demand by `collectReservedRects(hostRect)` from every DOM element carrying `data-panel-reserved`, normalized into host-relative `Rect`s.

**Sources** (each contributes one region):

| Source | File | Corner |
|---|---|---|
| Page top-cluster (theme + account) | `components/workspace-shell/WorkspaceOverlay.tsx` | top-right |
| Page right-stack (viewer tools) | `components/workspace-shell/WorkspaceOverlay.tsx` | top-right, below cluster |
| Page bottom-end (chat trigger) | `components/workspace-shell/WorkspaceOverlay.tsx` | bottom-right |
| Extension toolbar | `viewer/extensions/components/ExtensionToolbar.tsx` | top-right |
| Camera attitude widget | `features/solar/components/CameraAttitudeWidget.tsx` | top-right |
| Panel dock (reopen tray) | `viewer/panels/components/PanelDock.tsx` | left edge, centered |

**Relationships**: Consumed by `computeArrangement` as immovable obstacles. Every currently-open non-minimized panel, plus every `manuallyPlaced` panel, is appended to this set at arrangement time (see D5) — so "reserved region" at the algorithm's boundary means *anything the arrangement may not overlap*, whether it is chrome or a pinned panel.

---

## Arrangement Mode

An enum describing the outcome of one arrangement pass.

| Value | Meaning |
|---|---|
| `'grid'` | Every panel was placed with zero overlap against reserved regions and each other (FR-005a). |
| `'cascade'` | At least one panel could not be placed cleanly, so all auto-placed panels were re-laid as an offset stack (FR-005b). |

**State transitions**: Not persisted between passes — recomputed from scratch on every arrangement event (panel opened, panel closed, viewport resized, explicit arrange). Returned alongside positions so the caller can apply the mode's z-ordering rule (`cascade` → smallest-area frontmost, per D4; `grid` → z-order untouched, since nothing overlaps).

---

## Arrangement Result

The return value of `computeArrangement`. See [contracts/arrangement.md](./contracts/arrangement.md) for the full signature.

| Field | Type | Notes |
|---|---|---|
| `mode` | `ArrangementMode` | Which branch produced this result |
| `positions` | `Map<string, {x, y}>` | Panel id → host-relative position. Contains an entry for **every** panel passed in that was not `manuallyPlaced`. |
| `zOrder` | `Map<string, number>` \| `null` | Populated only in `'cascade'` mode (D4); `null` in `'grid'` mode, meaning "leave existing z-order alone". |

**Validation**: `positions` is total over the auto-placed input — FR-004 forbids omitting a panel because no good spot was found. Every returned position is clamped so the panel's full box lies within the host bounds.

---

## FloatingPanel (modified)

The existing entity in `viewer/panels/types/panel.ts` gains one field.

| Field | Type | Default | Notes |
|---|---|---|---|
| `manuallyPlaced` | `boolean` | `false` | **New.** Set `true` by `updatePosition`/`updateSize` (user gestures only). Never set by `applyArrangement` or `clampToViewport`. Cleared for all panels by the explicit arrange action (FR-005e). |

**State transitions**:

```text
opened (false)
  ── user drags / resizes / keyboard-nudges ──▶ true
  ── automatic arrangement runs ──────────────▶ unchanged (treated as an obstacle while true)
  ── explicit "arrange" action ───────────────▶ false (then re-placed)
  ── clampToViewport (window resize) ─────────▶ unchanged
```

All other `FloatingPanel` fields are unchanged.

---

## Reopen Tray Entry

One closed panel, retained so it can be recreated.

| Field | Type | Notes |
|---|---|---|
| `request` | `PanelRequest` | The existing discriminated union (`content` \| `live`), reconstructed from the panel at close time. Carries `requestId`, `title`, `chrome`, `contextAssociation`, plus `content` or `typeKey`+`data`. |
| `closedAtUtc` | `number` | Epoch ms from the store's existing monotonic `nextTimestamp()` helper — orders the tray and identifies the oldest entry when trimming. |

**Deliberately absent**: `position`, `size`, `zOrder`, `minimized`, `manuallyPlaced`. A reopened panel is placed fresh (spec Assumptions, D8).

**Validation / invariants**:
- Capped at `MAX_CONCURRENT_PANELS` (10). Adding an 11th drops the oldest (FR-010).
- Keyed by `request.requestId`. Closing a panel whose id already has an entry **replaces** that entry rather than duplicating it (Edge Case: closing a reopened panel again).
- Minimizing never produces an entry (FR-011).

**Relationships**: Reopening removes the entry and feeds `request` straight into the existing `openPanel(request)` — which is what makes validation, chrome resolution, and context-status derivation identical to a first-time open (D8, FR-009, FR-013).

---

## Drag Session (transient)

Component-local state in `FloatingPanelHost`, not store state — it exists only between drag start and drag stop.

| Field | Type | Notes |
|---|---|---|
| `panelId` | `string` | Which panel is being dragged |
| `candidateSlots` | `Rect[]` | Zero-overlap slots for this panel's size, computed **once** at drag start (D6) |
| `activeSlot` | `Rect \| null` | The slot under the pointer, or `null` when over none (FR-005g → render no placeholder) |

**Lifecycle**: created on drag start, `activeSlot` updated per pointer move, destroyed on drag stop (after snapping to `activeSlot` if non-null).
