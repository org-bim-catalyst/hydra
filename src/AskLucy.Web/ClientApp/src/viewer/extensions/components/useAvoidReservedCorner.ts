import { useEffect, useState, type RefObject } from 'react'
import { RESERVED_ATTRIBUTE } from '../../panels/layout/reservedRegions'

const DEFAULT_TOP = 16
const MARGIN = 8
/** How close to the viewport's left/right edge a reserved element has to be to count as sharing
 * that corner — wide enough to catch a tall stacked column even at its widest, narrow enough not
 * to pick up something on the opposite side of the screen. */
const CORNER_WIDTH = 320
/** How close to the viewport's TOP a reserved element has to be to count as sharing this (top-)
 * corner. Found live (2026-09-13): `PanelDock` is vertically centered (`top: '50%'`) but still
 * near the left edge horizontally, so without also checking vertical proximity, the corner-width
 * check alone swept it into "chrome to avoid in the top-left corner" — pushing
 * `CameraAttitudeWidget` down to PanelDock's mid-screen position instead of just below the
 * studio's top-left HUD row, which is what it's actually meant to clear. */
const CORNER_HEIGHT = 320

/** `ExtensionToolbar` and `CameraAttitudeWidget` both carry this, in addition to
 * `RESERVED_ATTRIBUTE` — it marks them as siblings in the same corner measurement, so a widget
 * never avoids another widget sharing its own corner (which would race against effect-ordering,
 * since both measure on the same mount) — each avoids only page-level chrome
 * (`WorkspaceOverlay`, including its top-left HUD row), which is what's
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
 * else occupies the corner. specs/073 research D9: in the top-left corner that is now a single
 * element — the HUD row's start group (Home, title, weather, boundary confidence), which carries
 * the attribute as a whole — so a left-corner widget sits just below one 40 px row rather than
 * below a stack of separately positioned cards.
 *
 * Found live (2026-09-13): a mount-once effect measures too early. Both callers call this hook
 * unconditionally (correct — hooks can't be conditional) but return `null` from their own render
 * until their content is actually ready (`ExtensionToolbar` waits for a contributed entry;
 * `CameraAttitudeWidget` waits for activation + a camera reading) — both of which resolve
 * *asynchronously*, after this hook's mount-time effect has already run once and settled on
 * `DEFAULT_TOP`. A `[selfRef, side]` dependency array never changes, so the effect never re-ran
 * once real content started rendering — the corner value was stuck at 16 forever, overlapping
 * whatever chrome was actually there. A `MutationObserver` re-measures whenever the reserved-chrome
 * set anywhere in the document actually changes (a new one mounts, `RESERVED_ATTRIBUTE` is added/
 * removed), which is what "whatever occupies this corner right now" actually requires — not just
 * mount-time and window-resize. Coalesced onto `requestAnimationFrame` so a burst of unrelated DOM
 * mutations elsewhere on the page triggers at most one re-measure per frame. */
export function useAvoidReservedCorner(selfRef: RefObject<HTMLElement | null>, side: 'left' | 'right' = 'right'): number {
  const [top, setTop] = useState(DEFAULT_TOP)

  useEffect(() => {
    let rafId: number | null = null

    const measure = () => {
      rafId = null
      const selfEl = selfRef.current
      const elements = document.querySelectorAll(`[${RESERVED_ATTRIBUTE}]`)
      let maxBottom = 0
      elements.forEach((element) => {
        if (element === selfEl) return
        if (element.hasAttribute(CORNER_CHROME_ATTRIBUTE)) return // a sibling corner widget, not page chrome
        const rect = element.getBoundingClientRect()
        if (rect.width <= 0 || rect.height <= 0) return
        const nearThisEdge = side === 'right' ? rect.right >= window.innerWidth - CORNER_WIDTH : rect.left <= CORNER_WIDTH
        const nearTop = rect.top <= CORNER_HEIGHT
        if (!nearThisEdge || !nearTop) return
        if (rect.bottom > maxBottom) maxBottom = rect.bottom
      })
      setTop(maxBottom > 0 ? maxBottom + MARGIN : DEFAULT_TOP)
    }

    const scheduleMeasure = () => {
      if (rafId !== null) return
      rafId = requestAnimationFrame(measure)
    }

    measure()
    window.addEventListener('resize', scheduleMeasure)

    const observer = new MutationObserver(scheduleMeasure)
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: [RESERVED_ATTRIBUTE] })

    return () => {
      if (rafId !== null) cancelAnimationFrame(rafId)
      window.removeEventListener('resize', scheduleMeasure)
      observer.disconnect()
    }
  }, [selfRef, side])

  return top
}
