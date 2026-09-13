import type { Rect } from '../types/panel'

/** contracts/reserved-regions.md — the attribute any chrome that must not be covered by a floating
 * panel carries. Exported so no consumer hardcodes the string (R1-R4). */
export const RESERVED_ATTRIBUTE = 'data-panel-reserved'

/** contracts/reserved-regions.md — collects every currently-declared reserved region, normalized
 * from viewport-relative `getBoundingClientRect()` coordinates into `hostRect`-relative ones (C2),
 * the same frame `FloatingPanel.position` is already interpreted in. This is the feature's only
 * DOM-aware code (D2) — queried fresh on demand (C1), never cached, so it cannot go stale. */
export function collectReservedRects(hostRect: { left: number; top: number }): Rect[] {
  if (typeof document === 'undefined') return []

  const elements = document.querySelectorAll(`[${RESERVED_ATTRIBUTE}]`)
  const rects: Rect[] = []

  elements.forEach((element) => {
    const box = element.getBoundingClientRect()
    if (box.width <= 0 || box.height <= 0) return // C3 — a hidden element must not pin an obstacle at the origin
    rects.push({ x: box.left - hostRect.left, y: box.top - hostRect.top, width: box.width, height: box.height })
  })

  return rects
}
