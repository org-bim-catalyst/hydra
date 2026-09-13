import { Box } from '@mui/material'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { ArrangeablePanel } from '../layout/arrangement'
import { computeArrangement, findCandidateSlots, slotAtPoint } from '../layout/arrangement'
import { collectReservedRects } from '../layout/reservedRegions'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import type { FloatingPanel as FloatingPanelModel, Rect } from '../types/panel'
import { FloatingPanel, MINIMIZED_BAR_HEIGHT, MINIMIZED_BAR_WIDTH } from './FloatingPanel'
import { LandingPlaceholder } from './LandingPlaceholder'
import { PanelDock } from './PanelDock'

/** specs/054 (feedback 2026-09-13) — a minimized panel's stored `size` is its pre-minimize
 * (restoreState) size, not its current on-screen footprint (`MINIMIZED_BAR_WIDTH/HEIGHT`), and it
 * is never itself an auto-placement target (`manuallyPlaced: true` unconditionally) — but IS a
 * fixed obstacle other panels must avoid, exactly like a manually-placed full panel, so both
 * `beginDrag` (candidate slots) and `runArrangement` (the grid/cascade pass) build their
 * `ArrangeablePanel` list through this one conversion, applied to every panel including minimized
 * ones (neither function filters minimized panels out anymore). */
function toArrangeablePanel(panel: FloatingPanelModel): ArrangeablePanel {
  return panel.minimized
    ? { id: panel.id, size: { width: MINIMIZED_BAR_WIDTH, height: MINIMIZED_BAR_HEIGHT }, manuallyPlaced: true, position: panel.position }
    : { id: panel.id, size: panel.size, manuallyPlaced: panel.manuallyPlaced, position: panel.position }
}

/** specs/054 D6/data-model.md "Drag Session" — component-local, not store state: which panel is
 * being dragged and its candidate landing slots, frozen once at drag start. */
interface DragSession {
  panelId: string
  candidateSlots: Rect[]
}

/** Mounted once over the viewer (`features/viewer/components/ViewerSurface.tsx`, FR-002). Renders
 * every open `floatingPanelStore` panel — `FloatingPanel` itself decides how to present a
 * minimized panel (a compact bar) vs. a normal one (full `react-rnd` chrome), so this host only
 * owns layout, not panel state. The host lets pointer events pass through to the viewer
 * everywhere except where an individual panel actually sits, so the viewer stays fully
 * interactive while any number of panels are open (FR-003) — mirroring how `WorkspaceOverlay`
 * layers over `ViewerSurface` in spec 027. It also re-clamps every panel's position back within
 * the current viewport on resize (FR-018, Edge Cases: viewport resize).
 *
 * specs/054: this is also the only place that runs the arrangement engine — it owns the host's DOM
 * ref, which is what `collectReservedRects` needs to normalize chrome rects into host-relative
 * coordinates (contracts/reserved-regions.md), and what `computeArrangement` needs for the host's
 * own bounds. The pure placement logic stays in `layout/arrangement.ts`; this component is only
 * the thin, DOM-aware glue that feeds it and applies its result. */
export function FloatingPanelHost() {
  const panels = useFloatingPanelStore((s) => s.panels)
  const clampToViewport = useFloatingPanelStore((s) => s.clampToViewport)
  const applyArrangement = useFloatingPanelStore((s) => s.applyArrangement)
  const arrangeAll = useFloatingPanelStore((s) => s.arrangeAll)
  const hostRef = useRef<HTMLDivElement>(null)

  // specs/054 D6 — the frozen candidate-slot list lives in a ref (it doesn't need to trigger a
  // re-render itself); only `activeSlot` does, since it drives whether/where `LandingPlaceholder`
  // renders.
  const dragSessionRef = useRef<DragSession | null>(null)
  const [activeSlot, setActiveSlot] = useState<Rect | null>(null)

  const beginDrag = useCallback((panelId: string) => {
    const hostEl = hostRef.current
    if (!hostEl) return
    const hostRect = hostEl.getBoundingClientRect()
    const reserved = collectReservedRects(hostRect)
    const arrangeable = useFloatingPanelStore.getState().panels.map(toArrangeablePanel)
    const candidateSlots = findCandidateSlots(
      { host: { width: hostRect.width, height: hostRect.height }, reserved, panels: arrangeable },
      panelId,
    )
    dragSessionRef.current = { panelId, candidateSlots }
    setActiveSlot(null)
  }, [])

  const trackDrag = useCallback((panelId: string, point: { x: number; y: number }) => {
    const session = dragSessionRef.current
    if (!session || session.panelId !== panelId) return
    setActiveSlot(slotAtPoint(session.candidateSlots, point))
  }, [])

  // specs/054 FR-002/FR-005: recomputes and applies a fresh grid/cascade arrangement. Reads panel
  // state via `getState()` rather than the `panels` selector above so this can be a stable
  // callback (safe in a `useEffect` dependency array) without re-subscribing to every panel change
  // — it is triggered explicitly (panel count, resize, explicit arrange, drop), not on every render.
  const runArrangement = useCallback(() => {
    const hostEl = hostRef.current
    if (!hostEl) return
    const hostRect = hostEl.getBoundingClientRect()
    if (hostRect.width <= 0 || hostRect.height <= 0) return

    const reserved = collectReservedRects(hostRect)
    const arrangeable = useFloatingPanelStore.getState().panels.map(toArrangeablePanel)

    const result = computeArrangement({
      host: { width: hostRect.width, height: hostRect.height },
      reserved,
      panels: arrangeable,
    })
    applyArrangement(result.positions, result.zOrder)
  }, [applyArrangement])

  // specs/054 FR-005f/g — `center` is the dragged panel's CENTER (feedback 2026-09-13: anchoring
  // to the top-left corner made the landing placeholder track far from wherever the user actually
  // grabbed the panel). Returns the shown placeholder's own top-left origin to snap to, or
  // null/undefined when none was showing — `FloatingPanel` falls back to the raw drop point in
  // that case, since `center` itself isn't a valid top-left position. Either way, the drop
  // reflows every other (non-pinned) panel around wherever this one just landed (feedback
  // 2026-09-13: "push each other while moving in the grid") — queued as a microtask so it runs
  // after `FloatingPanel`'s own `updatePosition` call for THIS drop (same synchronous call stack,
  // still ahead of the microtask queue), which is what makes this panel a fixed obstacle
  // (`manuallyPlaced: true`, set by `updatePosition`) for that reflow rather than something it
  // could move itself.
  const endDrag = useCallback(
    (panelId: string, center: { x: number; y: number }): { x: number; y: number } | null => {
      const session = dragSessionRef.current
      const slot = session && session.panelId === panelId ? slotAtPoint(session.candidateSlots, center) : null
      dragSessionRef.current = null
      setActiveSlot(null)
      queueMicrotask(() => runArrangement())
      return slot ? { x: slot.x, y: slot.y } : null
    },
    [runArrangement],
  )

  // specs/054 FR-002/FR-003: a fresh arrangement pass whenever the number of open panels changes
  // — covers opening a panel (including a reopen from the tray, US2), closing one, and closing a
  // minimized one (a minimized panel is a fixed obstacle other panels must avoid — feedback
  // 2026-09-13 — so one fewer of them can free up space for the rest, same as a full panel).
  // Keyed on count rather than the `panels` array itself so applying an arrangement's own position
  // writes (which don't change the count) never re-triggers this effect.
  const openPanelCount = panels.length
  useEffect(() => {
    runArrangement()
  }, [openPanelCount, runArrangement])

  useEffect(() => {
    const handleResize = () => {
      const bounds = hostRef.current?.getBoundingClientRect()
      if (bounds) {
        clampToViewport({ width: bounds.width, height: bounds.height })
      }
      // specs/054 FR-005: re-evaluate clear-area placement after clamping, so a resize doesn't
      // leave a panel newly hidden behind chrome that moved/resized along with the window.
      runArrangement()
    }
    window.addEventListener('resize', handleResize)
    return () => window.removeEventListener('resize', handleResize)
  }, [clampToViewport, runArrangement])

  // specs/054 FR-005e — the dock's "arrange" action: clear every panel's manuallyPlaced flag
  // (arrangeAll), then immediately run a fresh pass over the now-fully-unpinned panel set.
  // zustand's `set()` is synchronous, so runArrangement reads the just-cleared flags correctly.
  const handleArrange = useCallback(() => {
    arrangeAll()
    runArrangement()
  }, [arrangeAll, runArrangement])

  return (
    <Box ref={hostRef} sx={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }}>
      {panels.map((panel) => (
        <FloatingPanel
          key={panel.id}
          panel={panel}
          onDragStart={() => beginDrag(panel.id)}
          onDragMove={(point) => trackDrag(panel.id, point)}
          onDragEnd={(point) => endDrag(panel.id, point)}
        />
      ))}
      {activeSlot && <LandingPlaceholder slot={activeSlot} />}
      <PanelDock onArrange={handleArrange} />
    </Box>
  )
}
