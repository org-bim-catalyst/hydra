# Contract: Arrangement Engine

**Module**: `viewer/panels/layout/arrangement.ts`

**Nature**: Pure. No DOM access, no store access, no imports from React or Zustand. Same inputs always produce the same output. This is the entire placement policy for the feature — every caller (panel opened, panel closed, viewport resized, explicit arrange, drag placeholder) goes through the two functions below.

---

## `computeArrangement(input): ArrangementResult`

Decides where every auto-placed panel goes.

### Input

```ts
interface ArrangementInput {
  /** The host's own box, as {width, height}. Origin is implicitly (0,0). */
  host: { width: number; height: number }
  /** Chrome that must not be covered, host-relative (see contracts/reserved-regions.md). */
  reserved: Rect[]
  /** Every open, non-minimized panel. Minimized panels are excluded by the caller. */
  panels: ArrangeablePanel[]
}

interface ArrangeablePanel {
  id: string
  size: { width: number; height: number }
  /** When true this panel is NOT placed; its current box joins `reserved` instead (D5). */
  manuallyPlaced: boolean
  /** Required when `manuallyPlaced` is true, so its box can be treated as an obstacle. */
  position: { x: number; y: number }
}
```

### Output

```ts
interface ArrangementResult {
  mode: 'grid' | 'cascade'
  positions: Map<string, { x: number; y: number }>
  zOrder: Map<string, number> | null
}
```

### Behavioural requirements

| # | Requirement | Traces to |
|---|---|---|
| A1 | Every panel with `manuallyPlaced === false` appears in `positions`. No panel is ever omitted. | FR-004 |
| A2 | No panel in `positions` is placed over a `reserved` rect, over a `manuallyPlaced` panel's box, or over another placed panel — **whenever such an arrangement exists**. | FR-002, FR-005a |
| A3 | When A2 is achievable for all panels, `mode === 'grid'` and `zOrder === null`. | FR-005a |
| A4 | When A2 is not achievable for at least one panel, all auto-placed panels are re-laid as an offset cascade and `mode === 'cascade'`. | FR-005b |
| A5 | In `'cascade'` mode, `zOrder` ranks panels by area **descending** — largest gets the lowest z, smallest the highest. | FR-005c |
| A6 | Every returned position keeps the panel's full box within `host` (`0 ≤ x ≤ host.width − width`, same for y). Where the panel is larger than the host, it is pinned to `{0, 0}`. | FR-005, Edge Cases |
| A7 | Panels are considered for placement in **descending area order**, so large panels claim space first. | D3 |
| A8 | Placement is deterministic: identical input yields an identical `positions` map, including iteration order. | Testability |
| A9 | An empty `panels` array returns `mode: 'grid'`, an empty map, and `zOrder: null` — never throws. | §2-VIII |
| A10 | A `reserved` rect that fully covers the host does not throw; the cascade branch still returns clamped positions. | FR-004, Edge Cases |
| A11 | In the cascade branch (A4), placement anchors at the largest available clear region before offsetting, rather than an arbitrary fixed corner — so cascade never produces more overlap than the panel count/host size make unavoidable. | FR-004 |

### Algorithm (normative summary)

1. Partition `panels` into pinned (`manuallyPlaced`) and auto-placed. Append each pinned panel's box to the obstacle set.
2. Sort auto-placed panels by area descending (A7).
3. **Grid attempt** — for each panel in order, scan shelf-packed candidate origins across the host and take the first that overlaps no obstacle; on success add the placed box to the obstacle set. If every panel is placed, return `mode: 'grid'`, `zOrder: null` (A3).
4. **Cascade fallback** — if any panel failed, discard the grid result entirely and re-place *all* auto-placed panels at a fixed offset step from the largest clear anchor, wrapping before the far edge, clamped per A6. Assign `zOrder` by descending area (A5) and return `mode: 'cascade'` (A4).

Step 4 discarding step 3's partial result is deliberate: a half-grid, half-cascade layout reads as a bug rather than a mode.

---

## `findCandidateSlots(input, panelId): Rect[]`

Returns every zero-overlap position the given panel's size could occupy, as full rects. Used once per drag gesture (D6), never per pointer move.

### Behavioural requirements

| # | Requirement | Traces to |
|---|---|---|
| S1 | The dragged panel's own current box is excluded from the obstacle set — a panel never blocks itself. | D6 |
| S2 | Every returned rect overlaps no reserved region, no pinned panel, and no other open panel. | FR-005f |
| S3 | Every returned rect lies fully within `host`. | FR-005f |
| S4 | Returns `[]` when nothing is clear — the caller then shows no placeholder. | FR-005g |
| S5 | Slots are returned in a stable order, so the placeholder does not flicker between equivalent candidates. | SC-008 |

### Companion helper

```ts
function slotAtPoint(slots: Rect[], point: { x: number; y: number }): Rect | null
```

O(n) containment test over the frozen slot list — this is what runs on every `onDrag` event. Returns `null` when the pointer is inside no slot (FR-005g).

---

## Non-goals

- **No DOM reads.** Obstacles arrive as data; `reservedRegions.ts` is the only module that touches the DOM.
- **No store writes.** The caller applies the result via `applyArrangement`.
- **No animation or transition concerns.** Positions are final values; any easing is a rendering decision.
- **No persistence.** Results are recomputed, never cached across events.
