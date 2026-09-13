import { describe, expect, it } from 'vitest'
import type { Rect } from '../types/panel'
import { computeArrangement, findCandidateSlots, slotAtPoint, type ArrangeablePanel } from './arrangement'

const HOST = { width: 1000, height: 800 }

function panel(id: string, size: { width: number; height: number }, overrides: Partial<ArrangeablePanel> = {}): ArrangeablePanel {
  return { id, size, manuallyPlaced: false, position: { x: 0, y: 0 }, ...overrides }
}

function rectsOverlap(a: Rect, b: Rect): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y
}

describe('computeArrangement — grid mode (A1-A3, A6-A9)', () => {
  it('A9: returns an empty grid result for no panels, never throws', () => {
    const result = computeArrangement({ host: HOST, reserved: [], panels: [] })
    expect(result).toEqual({ mode: 'grid', positions: new Map(), zOrder: null })
  })

  it('A1/A3: places every auto-placed panel with zero overlap when they fit', () => {
    const panels = [panel('a', { width: 300, height: 200 }), panel('b', { width: 300, height: 200 }), panel('c', { width: 300, height: 200 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels })

    expect(result.mode).toBe('grid')
    expect(result.zOrder).toBeNull()
    expect(result.positions.size).toBe(3)

    const rects = panels.map((p) => {
      const pos = result.positions.get(p.id)!
      return { x: pos.x, y: pos.y, width: p.size.width, height: p.size.height }
    })
    for (let i = 0; i < rects.length; i++) {
      for (let j = i + 1; j < rects.length; j++) {
        expect(rectsOverlap(rects[i], rects[j])).toBe(false)
      }
    }
  })

  it('A2: does not place a panel over a reserved region', () => {
    const reserved: Rect[] = [{ x: 0, y: 0, width: 900, height: 100 }]
    const panels = [panel('a', { width: 200, height: 150 })]
    const result = computeArrangement({ host: HOST, reserved, panels })

    const pos = result.positions.get('a')!
    const rect = { x: pos.x, y: pos.y, width: 200, height: 150 }
    expect(rectsOverlap(rect, reserved[0])).toBe(false)
  })

  it('A6: clamps a panel larger than the host to just inside the host margin instead of throwing', () => {
    const panels = [panel('a', { width: 2000, height: 2000 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels })
    // HOST_MARGIN (16px) keeps even a clamped panel off the raw edge.
    expect(result.positions.get('a')).toEqual({ x: 16, y: 16 })
  })

  it('A7: places larger panels first, so a big panel does not get squeezed out by small ones claiming space first', () => {
    const big = panel('big', { width: 700, height: 600 })
    const smalls = [panel('s1', { width: 100, height: 100 }), panel('s2', { width: 100, height: 100 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels: [...smalls, big] })
    expect(result.mode).toBe('grid')
    expect(result.positions.size).toBe(3)
  })

  it('leaves at least a visual gap between two adjacently-packed panels (no touching edges)', () => {
    const panels = [panel('a', { width: 200, height: 150 }), panel('b', { width: 200, height: 150 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels })
    expect(result.mode).toBe('grid')

    const a = result.positions.get('a')!
    const b = result.positions.get('b')!
    // Whichever axis they were packed along, there must be a real gap between their edges, not
    // an exact touch (a 0px-gap regression would fail this).
    const horizontalGap = b.x - (a.x + 200)
    const verticalGap = b.y - (a.y + 150)
    expect(Math.max(horizontalGap, verticalGap)).toBeGreaterThan(0)
  })

  it('centers a small group vertically within a much taller host, rather than pinning it to the top', () => {
    const tallHost = { width: 1000, height: 2000 }
    const panels = [panel('a', { width: 200, height: 150 })]
    const result = computeArrangement({ host: tallHost, reserved: [], panels })
    const pos = result.positions.get('a')!
    // Roughly vertically centered: (2000 - 150) / 2 ≈ 925 — nowhere near the top-left default.
    expect(pos.y).toBeGreaterThan(500)
  })

  it('never centers a group into a collision with a reserved region — falls back to the valid unshifted position', () => {
    // Reserved chrome across the vertical center band; a naive center-shift would land the panel
    // inside it.
    const tallHost = { width: 1000, height: 2000 }
    const reserved: Rect[] = [{ x: 0, y: 800, width: 1000, height: 400 }]
    const panels = [panel('a', { width: 200, height: 150 })]
    const result = computeArrangement({ host: tallHost, reserved, panels })
    const pos = result.positions.get('a')!
    const rect = { x: pos.x, y: pos.y, width: 200, height: 150 }
    expect(rectsOverlap(rect, reserved[0])).toBe(false)
  })

  it('A8: identical input yields identical output', () => {
    const panels = [panel('a', { width: 300, height: 200 }), panel('b', { width: 250, height: 400 })]
    const r1 = computeArrangement({ host: HOST, reserved: [], panels })
    const r2 = computeArrangement({ host: HOST, reserved: [], panels })
    expect([...r1.positions.entries()]).toEqual([...r2.positions.entries()])
    expect(r1.mode).toBe(r2.mode)
  })
})

describe('computeArrangement — host margin (never touching the viewport edge)', () => {
  it('keeps a single auto-placed panel inset from every edge, not flush against them', () => {
    const panels = [panel('a', { width: 100, height: 100 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels })
    const pos = result.positions.get('a')!
    expect(pos.x).toBeGreaterThanOrEqual(16)
    expect(pos.y).toBeGreaterThanOrEqual(16)
    expect(pos.x + 100).toBeLessThanOrEqual(HOST.width - 16)
    expect(pos.y + 100).toBeLessThanOrEqual(HOST.height - 16)
  })

  it('keeps every panel inset from the edge in cascade mode too', () => {
    const tinyHost = { width: 300, height: 300 }
    const panels = [panel('a', { width: 250, height: 250 }), panel('b', { width: 250, height: 250 })]
    const result = computeArrangement({ host: tinyHost, reserved: [], panels })
    expect(result.mode).toBe('cascade')
    for (const pos of result.positions.values()) {
      expect(pos.x).toBeGreaterThanOrEqual(16)
      expect(pos.y).toBeGreaterThanOrEqual(16)
    }
  })
})

describe('computeArrangement — cascade fallback (A4, A5, A10, A11)', () => {
  it('A4: falls back to cascade when panels cannot all fit without overlap', () => {
    const tinyHost = { width: 500, height: 400 }
    const panels = [
      panel('a', { width: 400, height: 300 }),
      panel('b', { width: 400, height: 300 }),
      panel('c', { width: 400, height: 300 }),
    ]
    const result = computeArrangement({ host: tinyHost, reserved: [], panels })
    expect(result.mode).toBe('cascade')
    expect(result.positions.size).toBe(3)
    expect(result.zOrder).not.toBeNull()
  })

  it('A5: ranks z-order by area descending — the smallest panel gets the highest rank (frontmost)', () => {
    const tinyHost = { width: 300, height: 300 }
    const large = panel('large', { width: 280, height: 280 })
    const small = panel('small', { width: 90, height: 90 })
    const medium = panel('medium', { width: 180, height: 180 })
    const result = computeArrangement({ host: tinyHost, reserved: [], panels: [large, small, medium] })

    expect(result.mode).toBe('cascade')
    const zOrder = result.zOrder!
    expect(zOrder.get('small')!).toBeGreaterThan(zOrder.get('medium')!)
    expect(zOrder.get('medium')!).toBeGreaterThan(zOrder.get('large')!)
  })

  it('A6 (cascade): every position stays within host bounds', () => {
    const tinyHost = { width: 300, height: 300 }
    const panels = Array.from({ length: 6 }, (_, i) => panel(`p${i}`, { width: 250, height: 250 }))
    const result = computeArrangement({ host: tinyHost, reserved: [], panels })
    for (const pos of result.positions.values()) {
      expect(pos.x).toBeGreaterThanOrEqual(0)
      expect(pos.y).toBeGreaterThanOrEqual(0)
      expect(pos.x).toBeLessThanOrEqual(tinyHost.width)
      expect(pos.y).toBeLessThanOrEqual(tinyHost.height)
    }
  })

  it('A10: a reserved rect fully covering the host does not throw, and cascade still returns clamped positions', () => {
    const reserved: Rect[] = [{ x: 0, y: 0, width: HOST.width, height: HOST.height }]
    const panels = [panel('a', { width: 200, height: 150 })]
    expect(() => computeArrangement({ host: HOST, reserved, panels })).not.toThrow()
    const result = computeArrangement({ host: HOST, reserved, panels })
    expect(result.mode).toBe('cascade')
    const pos = result.positions.get('a')!
    expect(pos.x).toBeGreaterThanOrEqual(0)
    expect(pos.y).toBeGreaterThanOrEqual(0)
  })

  it('A11: cascade anchors at a genuinely free region rather than an arbitrary fixed corner', () => {
    // Left 200px is reserved; a 300x300 free area remains to its right. Three 200x200 panels
    // cannot all fit in that area without overlap (only one fits per row/column), so this falls
    // back to cascade — but the anchor must still land in the free area, not inside the reserved
    // region at x=0.
    const reserved: Rect[] = [{ x: 0, y: 0, width: 200, height: 300 }]
    const tinyHost = { width: 500, height: 300 }
    const panels = [panel('a', { width: 200, height: 200 }), panel('b', { width: 200, height: 200 }), panel('c', { width: 200, height: 200 })]
    const result = computeArrangement({ host: tinyHost, reserved, panels })
    expect(result.mode).toBe('cascade')
    // Every returned position's x is at or past the reserved region's right edge, proving the
    // anchor was found via a free-slot search rather than defaulting to {0,0}.
    for (const pos of result.positions.values()) {
      expect(pos.x).toBeGreaterThanOrEqual(200)
    }
  })
})

describe('computeArrangement — manually placed panels (D5)', () => {
  it('treats a manuallyPlaced panel as an obstacle, not a placement target', () => {
    const pinned = panel('pinned', { width: 400, height: 300 }, { manuallyPlaced: true, position: { x: 0, y: 0 } })
    const auto = panel('auto', { width: 200, height: 150 })
    const result = computeArrangement({ host: HOST, reserved: [], panels: [pinned, auto] })

    expect(result.positions.has('pinned')).toBe(false)
    expect(result.positions.size).toBe(1)
    const pos = result.positions.get('auto')!
    const autoRect = { x: pos.x, y: pos.y, width: 200, height: 150 }
    const pinnedRect = { x: 0, y: 0, width: 400, height: 300 }
    expect(rectsOverlap(autoRect, pinnedRect)).toBe(false)
  })
})

describe('computeArrangement — FR-012 parity (content vs. live panels)', () => {
  it('places panels identically regardless of any notion of "kind" — the engine has no such field', () => {
    // The ArrangeablePanel type carries no `kind` — this test documents that placement is
    // structurally identical for what would be a content panel and a live panel upstream.
    const panels = [panel('content-like', { width: 300, height: 200 }), panel('live-like', { width: 300, height: 200 })]
    const result = computeArrangement({ host: HOST, reserved: [], panels })
    const a = result.positions.get('content-like')!
    const b = result.positions.get('live-like')!
    expect(a).not.toEqual(b)
    expect(result.positions.size).toBe(2)
  })
})

describe('findCandidateSlots / slotAtPoint (S1-S5, FR-005f/g)', () => {
  it('S1: excludes the dragged panel itself from the obstacle set', () => {
    // Reserved regions cover every part of the host EXCEPT exactly where `dragged` currently sits
    // (16,16,200,150) — inset from the origin by HOST_MARGIN, with enough extra margin that
    // PANEL_GAP's own obstacle inflation still leaves that exact pocket free — so a slot can only
    // exist there if `dragged`'s own box is excluded from the obstacle set. If it were (wrongly)
    // included as an obstacle, this would return [].
    const GAP = 16
    const MARGIN = 16
    const dragged = panel('dragged', { width: 200, height: 150 }, { position: { x: MARGIN, y: MARGIN } })
    const reserved: Rect[] = [
      { x: MARGIN + 200 + GAP, y: 0, width: HOST.width - (MARGIN + 200 + GAP), height: HOST.height },
      { x: 0, y: MARGIN + 150 + GAP, width: HOST.width, height: HOST.height - (MARGIN + 150 + GAP) },
    ]
    const slots = findCandidateSlots({ host: HOST, reserved, panels: [dragged] }, 'dragged')
    expect(slots.some((s) => s.x === MARGIN && s.y === MARGIN && s.width === 200 && s.height === 150)).toBe(true)
  })

  it('S2/S3: every returned slot overlaps no reserved region, no other panel, and stays within host', () => {
    const reserved: Rect[] = [{ x: 0, y: 0, width: 300, height: 800 }]
    const other = panel('other', { width: 200, height: 200 }, { position: { x: 400, y: 0 } })
    const dragged = panel('dragged', { width: 150, height: 150 }, { position: { x: 700, y: 0 } })
    const slots = findCandidateSlots({ host: HOST, reserved, panels: [other, dragged] }, 'dragged')

    expect(slots.length).toBeGreaterThan(0)
    for (const slot of slots) {
      expect(rectsOverlap(slot, reserved[0])).toBe(false)
      expect(rectsOverlap(slot, { x: 400, y: 0, width: 200, height: 200 })).toBe(false)
      expect(slot.x + slot.width).toBeLessThanOrEqual(HOST.width)
      expect(slot.y + slot.height).toBeLessThanOrEqual(HOST.height)
    }
  })

  it('S4: returns an empty array when nothing is clear for the panel size', () => {
    const reserved: Rect[] = [{ x: 0, y: 0, width: HOST.width, height: HOST.height }]
    const dragged = panel('dragged', { width: 100, height: 100 }, { position: { x: 0, y: 0 } })
    const slots = findCandidateSlots({ host: HOST, reserved, panels: [dragged] }, 'dragged')
    expect(slots).toEqual([])
  })

  it('returns [] for an unknown panel id rather than throwing', () => {
    expect(findCandidateSlots({ host: HOST, reserved: [], panels: [] }, 'missing')).toEqual([])
  })

  it('slotAtPoint finds the containing slot, or null when the point is in none', () => {
    const slots: Rect[] = [{ x: 0, y: 0, width: 100, height: 100 }, { x: 200, y: 0, width: 100, height: 100 }]
    expect(slotAtPoint(slots, { x: 50, y: 50 })).toEqual(slots[0])
    expect(slotAtPoint(slots, { x: 250, y: 50 })).toEqual(slots[1])
    expect(slotAtPoint(slots, { x: 150, y: 50 })).toBeNull()
  })
})
