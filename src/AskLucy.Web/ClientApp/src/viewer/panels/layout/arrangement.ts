import type { ArrangementMode, Rect } from '../types/panel'

/** contracts/arrangement.md — a panel as the pure arrangement engine sees it: just enough to place
 * it, nothing about its content, chrome, or kind (FR-012 falls out of this for free — the engine
 * cannot distinguish a content panel from a live one, so it can't treat them differently). */
export interface ArrangeablePanel {
  id: string
  size: { width: number; height: number }
  /** When true this panel is NOT placed; its current box joins the obstacle set instead (D5). */
  manuallyPlaced: boolean
  position: { x: number; y: number }
}

export interface ArrangementInput {
  /** The host's own box, as {width, height}. Origin is implicitly (0,0). */
  host: { width: number; height: number }
  /** Chrome that must not be covered, host-relative (contracts/reserved-regions.md). */
  reserved: Rect[]
  /** Every open, non-minimized panel. Minimized panels are excluded by the caller. */
  panels: ArrangeablePanel[]
}

export interface ArrangementResult {
  mode: ArrangementMode
  positions: Map<string, { x: number; y: number }>
  /** Populated only in `'cascade'` mode (D4); `null` in `'grid'` mode — leave z-order alone. */
  zOrder: Map<string, number> | null
}

const CASCADE_STEP = 32
const CASCADE_STEPS_BEFORE_WRAP = 10
/** Visual breathing room the grid keeps between every panel, and between a panel and any reserved
 * region — found live: a top-left-anchored pack with no margin renders panels touching edge to
 * edge, which reads as broken chrome rather than an intentional grid. Applied by inflating every
 * obstacle (reserved regions, pinned panels, and each already-placed panel) by this amount before
 * the next panel is placed against it — so the panel side and the obstacle side each contribute
 * half, yielding one full `PANEL_GAP` of clear space between any two placed rectangles. */
const PANEL_GAP = 16
/** Found live: an auto-placed panel could land flush against the viewport edge, which read as a
 * layout bug rather than an intentional edge. Modeled as four thin obstacle strips around the
 * host's border (`marginObstacles`) rather than threaded through every bound check individually —
 * it reuses the exact same overlap-avoidance logic every other obstacle already goes through. */
export const HOST_MARGIN = 16

function marginObstacles(host: { width: number; height: number }): Rect[] {
  return [
    { x: 0, y: 0, width: host.width, height: HOST_MARGIN },
    { x: 0, y: host.height - HOST_MARGIN, width: host.width, height: HOST_MARGIN },
    { x: 0, y: 0, width: HOST_MARGIN, height: host.height },
    { x: host.width - HOST_MARGIN, y: 0, width: HOST_MARGIN, height: host.height },
  ]
}

function area(size: { width: number; height: number }): number {
  return size.width * size.height
}

function rectsOverlap(a: Rect, b: Rect): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y
}

/** Grows `rect` by `margin` on every side — used to reserve breathing room around an obstacle
 * without changing the true size used anywhere else (only the copy pushed into the obstacle list
 * is inflated). */
function inflate(rect: Rect, margin: number): Rect {
  return { x: rect.x - margin, y: rect.y - margin, width: rect.width + margin * 2, height: rect.height + margin * 2 }
}

function overlapsAny(candidate: Rect, obstacles: Rect[]): boolean {
  return obstacles.some((obstacle) => rectsOverlap(candidate, obstacle))
}

function clampToHost(rect: Rect, host: { width: number; height: number }): Rect {
  const width = Math.min(rect.width, Math.max(0, host.width - HOST_MARGIN * 2))
  const height = Math.min(rect.height, Math.max(0, host.height - HOST_MARGIN * 2))
  const x = Math.min(Math.max(rect.x, HOST_MARGIN), Math.max(HOST_MARGIN, host.width - HOST_MARGIN - width))
  const y = Math.min(Math.max(rect.y, HOST_MARGIN), Math.max(HOST_MARGIN, host.height - HOST_MARGIN - height))
  return { x, y, width, height }
}

/** Guillotine-style candidate points: every obstacle's right/bottom edge, plus the host's own
 * origin. Scanning only these (rather than every pixel) is what keeps placement fast and keeps the
 * result aligned to existing rectangles instead of landing at arbitrary sub-pixel offsets. */
function candidateOrigins(host: { width: number; height: number }, obstacles: Rect[]): { x: number; y: number }[] {
  const xs = new Set<number>([0])
  const ys = new Set<number>([0])
  for (const obstacle of obstacles) {
    xs.add(obstacle.x + obstacle.width)
    ys.add(obstacle.y + obstacle.height)
  }
  const origins: { x: number; y: number }[] = []
  const sortedYs = [...ys].filter((y) => y < host.height).sort((a, b) => a - b)
  const sortedXs = [...xs].filter((x) => x < host.width).sort((a, b) => a - b)
  for (const y of sortedYs) {
    for (const x of sortedXs) {
      origins.push({ x, y })
    }
  }
  return origins
}

/** The first zero-overlap slot for `size`, scanning top-to-bottom, left-to-right (S5: stable
 * order). Returns `null` when nothing fits without overlap anywhere in the host. */
function findFirstFreeSlot(
  host: { width: number; height: number },
  obstacles: Rect[],
  size: { width: number; height: number },
): Rect | null {
  for (const origin of candidateOrigins(host, obstacles)) {
    if (origin.x + size.width > host.width || origin.y + size.height > host.height) continue
    const candidate: Rect = { x: origin.x, y: origin.y, width: size.width, height: size.height }
    if (!overlapsAny(candidate, obstacles)) return candidate
  }
  return null
}

/** Every zero-overlap slot for `size` (not just the first) — used by `findCandidateSlots` (D6),
 * which needs the full set so the drag placeholder can track the pointer between them. */
function findAllFreeSlots(
  host: { width: number; height: number },
  obstacles: Rect[],
  size: { width: number; height: number },
): Rect[] {
  const slots: Rect[] = []
  for (const origin of candidateOrigins(host, obstacles)) {
    if (origin.x + size.width > host.width || origin.y + size.height > host.height) continue
    const candidate: Rect = { x: origin.x, y: origin.y, width: size.width, height: size.height }
    if (!overlapsAny(candidate, obstacles)) slots.push(candidate)
  }
  return slots
}

function pinnedObstacles(panels: ArrangeablePanel[]): Rect[] {
  return panels
    .filter((p) => p.manuallyPlaced)
    .map((p) => ({ x: p.position.x, y: p.position.y, width: p.size.width, height: p.size.height }))
}

/** Found live: a top-left-anchored grid pack left the whole group crowded into one corner with
 * the rest of the viewer empty. Shifts every auto-placed panel down by the same amount so the
 * group's vertical center lands at the host's vertical center, leaving the packing (and every
 * pairwise gap) otherwise untouched — it's a rigid shift, so it can never introduce a NEW overlap
 * between two placed panels. The only thing it could break is a collision with a *fixed* obstacle
 * (reserved chrome or a manually-placed panel), so the shift is verified against those before
 * being accepted; on any collision (or if it wouldn't fit within the host vertically) the
 * unshifted, already-valid positions are returned unchanged rather than risk it. */
function centerGroupVertically(
  positions: Map<string, { x: number; y: number }>,
  panels: ArrangeablePanel[],
  host: { width: number; height: number },
  fixedObstacles: Rect[],
): Map<string, { x: number; y: number }> {
  if (panels.length === 0) return positions

  let minY = Infinity
  let maxY = -Infinity
  for (const panel of panels) {
    const pos = positions.get(panel.id)!
    minY = Math.min(minY, pos.y)
    maxY = Math.max(maxY, pos.y + panel.size.height)
  }
  const groupHeight = maxY - minY
  const idealMinY = Math.max(HOST_MARGIN, Math.floor((host.height - groupHeight) / 2))
  const shiftY = idealMinY - minY
  if (shiftY === 0) return positions

  const shifted = new Map<string, { x: number; y: number }>()
  for (const panel of panels) {
    const pos = positions.get(panel.id)!
    shifted.set(panel.id, { x: pos.x, y: pos.y + shiftY })
  }

  const wouldCollide = panels.some((panel) => {
    const pos = shifted.get(panel.id)!
    if (pos.y < HOST_MARGIN || pos.y + panel.size.height > host.height - HOST_MARGIN) return true
    const rect: Rect = { x: pos.x, y: pos.y, width: panel.size.width, height: panel.size.height }
    return overlapsAny(rect, fixedObstacles)
  })

  return wouldCollide ? positions : shifted
}

/** Largest-area-first (D3/A7) — placing big panels while space is plentiful is what makes tight
 * cases fit at all, and it feeds directly into the cascade z-order rule (A5/D4). Sort is stabilized
 * by original index so identical-area panels keep a deterministic, input-order-preserving result
 * (A8), independent of whether the JS engine's `Array.sort` is stable. */
function byAreaDescending(panels: ArrangeablePanel[]): ArrangeablePanel[] {
  return panels
    .map((panel, index) => ({ panel, index }))
    .sort((a, b) => area(b.panel.size) - area(a.panel.size) || a.index - b.index)
    .map(({ panel }) => panel)
}

/** contracts/arrangement.md — the entire placement policy. Pure: no DOM, no store, same inputs
 * always produce the same output (A8). */
export function computeArrangement(input: ArrangementInput): ArrangementResult {
  const { host, reserved, panels } = input
  const baseObstacles = [
    ...marginObstacles(host),
    ...[...reserved, ...pinnedObstacles(panels)].map((rect) => inflate(rect, PANEL_GAP)),
  ]
  const autoPlaced = byAreaDescending(panels.filter((p) => !p.manuallyPlaced))

  if (autoPlaced.length === 0) {
    return { mode: 'grid', positions: new Map(), zOrder: null }
  }

  // Grid attempt (A2/A3): every panel must land with zero overlap, or the whole attempt is
  // discarded in favor of cascade (A4) — a half-grid, half-cascade result would read as a bug.
  const gridObstacles = [...baseObstacles]
  const gridPositions = new Map<string, { x: number; y: number }>()
  let gridSucceeded = true
  for (const panel of autoPlaced) {
    const slot = findFirstFreeSlot(host, gridObstacles, panel.size)
    if (!slot) {
      gridSucceeded = false
      break
    }
    gridPositions.set(panel.id, { x: slot.x, y: slot.y })
    gridObstacles.push(inflate(slot, PANEL_GAP))
  }
  if (gridSucceeded) {
    return { mode: 'grid', positions: centerGroupVertically(gridPositions, autoPlaced, host, baseObstacles), zOrder: null }
  }

  // Cascade fallback (A4/A11): anchor at the largest available clear region rather than an
  // arbitrary fixed corner, then offset each subsequent panel from there, wrapping before the far
  // edge. Anchored via the same free-slot search, probed with the largest panel's own size so the
  // anchor is a region that could actually hold something, not just an empty point.
  const anchorProbeSize = autoPlaced[0].size
  const anchor = findFirstFreeSlot(host, baseObstacles, anchorProbeSize) ?? { x: 0, y: 0 }

  const positions = new Map<string, { x: number; y: number }>()
  autoPlaced.forEach((panel, i) => {
    const step = i % CASCADE_STEPS_BEFORE_WRAP
    const raw: Rect = {
      x: anchor.x + step * CASCADE_STEP,
      y: anchor.y + step * CASCADE_STEP,
      width: panel.size.width,
      height: panel.size.height,
    }
    const clamped = clampToHost(raw, host)
    positions.set(panel.id, { x: clamped.x, y: clamped.y })
  })

  // A5/D4: rank by area descending — largest gets the lowest z (rank 1), smallest the highest
  // (rank N) — so whichever panel ends up underneath is always the larger of the pair, keeping
  // something of it reachable. The caller (the store) translates these ranks into real zOrder
  // integers that don't collide with existing panels' z-order.
  const zOrder = new Map<string, number>()
  autoPlaced.forEach((panel, rank) => zOrder.set(panel.id, rank + 1))

  return { mode: 'cascade', positions, zOrder }
}

/** contracts/arrangement.md — every zero-overlap slot the named panel's size could occupy right
 * now, excluding the panel's own current box (S1) so a panel never blocks itself. Frozen once per
 * drag gesture (D6), never recomputed per pointer move. */
export function findCandidateSlots(input: ArrangementInput, panelId: string): Rect[] {
  const target = input.panels.find((p) => p.id === panelId)
  if (!target) return []

  const obstacles = [
    ...marginObstacles(input.host),
    ...[
      ...input.reserved,
      ...input.panels
        .filter((p) => p.id !== panelId)
        .map((p) => ({ x: p.position.x, y: p.position.y, width: p.size.width, height: p.size.height })),
    ].map((rect) => inflate(rect, PANEL_GAP)),
  ]

  return findAllFreeSlots(input.host, obstacles, target.size)
}

/** O(n) containment test over a frozen slot list (D6) — this is what runs on every `onDrag` event,
 * not `findCandidateSlots` itself. Returns `null` when the pointer is inside no slot (FR-005g). */
export function slotAtPoint(slots: Rect[], point: { x: number; y: number }): Rect | null {
  return (
    slots.find(
      (slot) => point.x >= slot.x && point.x < slot.x + slot.width && point.y >= slot.y && point.y < slot.y + slot.height,
    ) ?? null
  )
}
