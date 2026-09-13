import { useEffect, useState, type RefObject } from 'react'
import { RESERVED_ATTRIBUTE } from '../../panels/layout/reservedRegions'

const DEFAULT_TOP = 16
const MARGIN = 8
/** How close to the viewport's left/right edge a reserved element has to be to count as sharing
 * that corner — wide enough to catch a tall stacked column even at its widest, narrow enough not
 * to pick up something on the opposite side of the screen. */
const CORNER_WIDTH = 320

/** `ExtensionToolbar` and `CameraAttitudeWidget` both carry this, in addition to
 * `RESERVED_ATTRIBUTE` — it marks them as siblings in the same corner measurement, so a widget
 * never avoids another widget sharing its own corner (which would race against effect-ordering,
 * since both measure on the same mount) — each avoids only page-level chrome
 * (`WorkspaceOverlay`/`LocationWeatherWidget`/`SiteBoundaryConfidenceBadge`), which is what's
 * actually unbounded. Two widgets sharing one corner instead use a small fixed relative offset
 * (see the callers), not a second dynamic measurement against each other. */
export const CORNER_CHROME_ATTRIBUTE = 'data-viewer-corner-chrome'

/** specs/054 D9 — chrome that lives in a screen corner also claimed by the page's own
 * `WorkspaceOverlay` chrome or another top-level widget, with no coordination between the systems
 * that place them. A prior stopgap pushed such chrome down by a hardcoded pixel guess — fragile
 * against a taller stack in the future, and already wrong once the other side's controls changed.
 *
 * This measures whatever else in the given corner currently carries `data-panel-reserved`
 * (contracts/reserved-regions.md — the same convention floating panels use to avoid this chrome)
 * and sits just below the tallest of it, falling back to the natural corner margin when nothing
 * else occupies the corner. Re-measures on mount and on window resize; the chrome being avoided
 * is fixed per session (research D9), so those two triggers are sufficient. */
export function useAvoidReservedCorner(selfRef: RefObject<HTMLElement | null>, side: 'left' | 'right' = 'right'): number {
  const [top, setTop] = useState(DEFAULT_TOP)

  useEffect(() => {
    const measure = () => {
      const selfEl = selfRef.current
      const elements = document.querySelectorAll(`[${RESERVED_ATTRIBUTE}]`)
      let maxBottom = 0
      elements.forEach((element) => {
        if (element === selfEl) return
        if (element.hasAttribute(CORNER_CHROME_ATTRIBUTE)) return // a sibling corner widget, not page chrome
        const rect = element.getBoundingClientRect()
        if (rect.width <= 0 || rect.height <= 0) return
        const inThisCorner = side === 'right' ? rect.right >= window.innerWidth - CORNER_WIDTH : rect.left <= CORNER_WIDTH
        if (!inThisCorner) return
        if (rect.bottom > maxBottom) maxBottom = rect.bottom
      })
      setTop(maxBottom > 0 ? maxBottom + MARGIN : DEFAULT_TOP)
    }
    measure()
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [selfRef, side])

  return top
}
