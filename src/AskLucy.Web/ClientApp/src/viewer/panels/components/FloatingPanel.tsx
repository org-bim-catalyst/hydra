import {
  RiCloseLine,
  RiDraggable,
  RiErrorWarningLine,
  RiExpandDiagonal2Line,
  RiExpandDiagonalLine,
  RiMapPinLine,
  RiSubtractLine,
} from '@remixicon/react'
import { Box, IconButton, Tooltip, Typography, alpha } from '@mui/material'
import { Rnd } from 'react-rnd'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { PanelDensityContext } from '../chrome/density'
import { ContentRenderer } from '../content/ContentRenderer'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { usePanelPreferencesStore } from '../store/panelPreferencesStore'
import { MIN_PANEL_HEIGHT, MIN_PANEL_WIDTH, type FloatingPanel as FloatingPanelModel } from '../types/panel'

const DRAG_HANDLE_CLASS = 'floating-panel-drag-handle'
// specs/054 (feedback 2026-09-13): exported — FloatingPanelHost needs the minimized bar's actual
// on-screen footprint to compute correct drag candidate slots/obstacles for it, since a minimized
// panel's stored `size` field still holds its pre-minimize (full) dimensions (restoreState.size),
// not what it currently occupies on screen.
export const MINIMIZED_BAR_WIDTH = 220
export const MINIMIZED_BAR_HEIGHT = 40
const NUDGE_STEP = 10
const NUDGE_STEP_LARGE = 40

/** The footer's resize cell: the 14px grip plus 6px either side. */
const FOOTER_RESIZE_CELL_WIDTH = 26

/** `react-rnd`'s drag is pointer/touch-only (no keyboard equivalent built in — the same posture
 * as most drag-resize libraries). This gives keyboard users a way to reposition a panel: focus the
 * title bar, then arrow keys (Shift+arrow for a larger step) nudge it, constrained to never go
 * negative (matching `bounds="parent"`'s intent for pointer dragging). Resizing has no keyboard
 * equivalent — react-rnd's resize handles are plain, non-focusable elements; a keyboard user can
 * still fully read/interact with every panel's content, only fine-grained resizing needs a
 * pointer, the same limitation `react-rnd` itself has out of the box. */
function nudgePosition(
  event: React.KeyboardEvent,
  panel: FloatingPanelModel,
  updatePosition: (id: string, position: { x: number; y: number }) => void,
) {
  const step = event.shiftKey ? NUDGE_STEP_LARGE : NUDGE_STEP
  let dx = 0
  let dy = 0
  if (event.key === 'ArrowLeft') dx = -step
  else if (event.key === 'ArrowRight') dx = step
  else if (event.key === 'ArrowUp') dy = -step
  else if (event.key === 'ArrowDown') dy = step
  else return

  event.preventDefault()
  updatePosition(panel.id, { x: Math.max(0, panel.position.x + dx), y: Math.max(0, panel.position.y + dy) })
}

export interface FloatingPanelProps {
  panel: FloatingPanelModel
  /** specs/054 D6 — fired when a drag gesture begins, so `FloatingPanelHost` can compute this
   * panel's candidate landing slots once, up front. */
  onDragStart?: () => void
  /** specs/054 D6 — fired on every drag move with the panel's current CENTER (not its top-left
   * corner — feedback 2026-09-13: anchoring slot detection to the corner meant the landing
   * placeholder tracked far from wherever the user actually grabbed the panel, for anything but a
   * tiny panel or a corner grab), so the host can look up and render whichever candidate slot the
   * panel's center is currently over. */
  onDragMove?: (center: { x: number; y: number }) => void
  /** specs/054 D6 — given the drop point's CENTER, returns the top-left position to snap to when
   * a landing placeholder was showing there, or `null`/`undefined` to leave the panel exactly
   * where it was released (free-form drop, unchanged from prior behavior). */
  onDragEnd?: (center: { x: number; y: number }) => { x: number; y: number } | null | undefined
}

/** Dispatches on the panel's own `kind` (specs/049): a content panel's already-validated block
 * document renders through the one general `ContentRenderer`; a live panel keeps resolving its
 * renderer from the (now narrowed, specs/049 FR-022) registry exactly as every panel did before
 * this feature. The `unknown-type`/`invalid` fallbacks below apply only to live panels — a
 * content panel's per-block degradation happens inside `ContentRenderer` itself, never here. */
function PanelContent({ panel }: { panel: FloatingPanelModel }) {
  if (panel.kind === 'content') {
    return panel.content ? <ContentRenderer content={panel.content} /> : null
  }

  const definition = panelTypeRegistry.resolve(panel.typeKey ?? '')
  const Renderer = definition?.renderer

  if (panel.validationStatus === 'unknown-type') {
    return (
      <Typography variant="body2" color="text.secondary">
        Unsupported panel type &quot;{panel.typeKey}&quot;.
      </Typography>
    )
  }

  if (panel.validationStatus === 'invalid') {
    return (
      <Box>
        <Typography variant="body2" color="text.secondary">
          This panel&apos;s data couldn&apos;t be loaded.
        </Typography>
        {panel.validationError && (
          <Typography component="details" variant="caption" color="text.disabled" sx={{ mt: 1 }}>
            <Box component="summary" sx={{ cursor: 'pointer' }}>
              Details
            </Box>
            {panel.validationError}
          </Typography>
        )}
      </Box>
    )
  }

  return Renderer ? <Renderer data={panel.data} /> : null
}

/** FR-013/FR-014 (User Story 4) — generic to every panel type (the association lives on the
 * `FloatingPanel` itself, data-model.md, not the type's renderer), so a "Locate" affordance and a
 * stale/invalid indicator work identically whether the panel is `summary`, `chart`, or any future
 * type that carries a `contextAssociation`. */
function ContextAssociationControls({ panel }: { panel: FloatingPanelModel }) {
  const layerId = panel.contextAssociation?.layerId
  const elementId = panel.contextAssociation?.elementId

  return (
    <>
      {panel.contextStatus === 'stale' && (
        <Tooltip title="This panel's viewer association may be outdated">
          <Box
            component="span"
            role="img"
            aria-label="Association is stale"
            sx={{ display: 'inline-flex', color: 'warning.main' }}
          >
            <RiErrorWarningLine size={16} />
          </Box>
        </Tooltip>
      )}
      {panel.contextStatus === 'invalid' && (
        <Tooltip title="This panel's viewer association no longer exists">
          <Box
            component="span"
            role="img"
            aria-label="Association is no longer valid"
            sx={{ display: 'inline-flex', color: 'error.main' }}
          >
            <RiErrorWarningLine size={16} />
          </Box>
        </Tooltip>
      )}
      {layerId && elementId && (
        <IconButton onClick={() => viewerEngine.select(layerId, elementId)} aria-label="Locate in viewer" size="small">
          <RiMapPinLine size={18} />
        </IconButton>
      )}
    </>
  )
}

/** The chrome for a single open AI-requested panel (data-model.md "Floating Panel"). Normal
 * (non-minimized) panels are wrapped in `react-rnd` for drag/resize (FR-004/FR-005), bounded to
 * the viewer surface (`bounds="parent"`, FR-018) with a minimum usable size (Edge Cases). A
 * minimized panel renders as a small bar instead (FR-006). This component is intentionally
 * namespaced under `viewer/panels/` rather than reusing `components/workspace-shell/FloatingPanel.tsx`,
 * an unrelated single-instance workspace-control drawer (research.md Decision 5). */
export function FloatingPanel({ panel, onDragStart, onDragMove, onDragEnd }: FloatingPanelProps) {
  const closePanel = useFloatingPanelStore((s) => s.closePanel)
  const focusPanel = useFloatingPanelStore((s) => s.focusPanel)
  const minimizePanel = useFloatingPanelStore((s) => s.minimizePanel)
  const restorePanel = useFloatingPanelStore((s) => s.restorePanel)
  const updatePosition = useFloatingPanelStore((s) => s.updatePosition)
  const updateSize = useFloatingPanelStore((s) => s.updateSize)
  const opacityPercent = usePanelPreferencesStore((s) => s.opacityPercent)

  const density = panel.chrome.density ?? 'comfortable'
  const compact = density === 'compact'
  const controlIconSize = compact ? 15 : 18

  const backgroundColor = (theme: { palette: { background: { paper: string } } }) =>
    alpha(theme.palette.background.paper, opacityPercent / 100)

  if (panel.minimized) {
    return (
      // Found live (2026-09-13): this used to be a plain positioned Box, not wrapped in `Rnd` at
      // all — a deliberate spec-049 simplification ("simpler, avoids ambiguity about what
      // dragging a minimized panel even means"), but with several panels now able to minimize
      // into overlapping bars with no collision avoidance of their own, that gap became a real
      // usability problem. Wired through the same onDragStart/onDragMove/onDragEnd chain the
      // full panel uses, so the identical landing-placeholder and reflow-on-drop behavior applies
      // here too — no new mechanism, just the existing one extended to this branch.
      <Rnd
        size={{ width: MINIMIZED_BAR_WIDTH, height: MINIMIZED_BAR_HEIGHT }}
        position={{ x: panel.position.x, y: panel.position.y }}
        bounds="parent"
        enableResizing={false}
        style={{ zIndex: panel.zOrder, pointerEvents: 'auto' }}
        onMouseDown={() => focusPanel(panel.id)}
        onDragStart={() => onDragStart?.()}
        onDrag={(_event, data) => {
          updatePosition(panel.id, { x: data.x, y: data.y })
          onDragMove?.({ x: data.x + MINIMIZED_BAR_WIDTH / 2, y: data.y + MINIMIZED_BAR_HEIGHT / 2 })
        }}
        onDragStop={(_event, data) => {
          const center = { x: data.x + MINIMIZED_BAR_WIDTH / 2, y: data.y + MINIMIZED_BAR_HEIGHT / 2 }
          const finalPosition = onDragEnd?.(center) ?? { x: data.x, y: data.y }
          updatePosition(panel.id, finalPosition)
        }}
      >
        <Box
          role="region"
          aria-label={panel.title}
          sx={{
            width: '100%',
            height: '100%',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            px: 1,
            borderRadius: 2,
            boxShadow: 4,
            bgcolor: backgroundColor,
            color: 'text.primary',
            cursor: 'move',
          }}
        >
          <Typography variant="caption" noWrap sx={{ flex: 1, minWidth: 0 }}>
            {panel.title}
          </Typography>
          <IconButton onClick={() => restorePanel(panel.id)} aria-label="Restore panel" size="small">
            <RiExpandDiagonalLine size={16} />
          </IconButton>
          <IconButton onClick={() => closePanel(panel.id)} aria-label="Close panel" size="small">
            <RiCloseLine size={16} />
          </IconButton>
        </Box>
      </Rnd>
    )
  }

  return (
    <Rnd
      size={{ width: panel.size.width, height: panel.size.height }}
      position={{ x: panel.position.x, y: panel.position.y }}
      bounds="parent"
      dragHandleClassName={DRAG_HANDLE_CLASS}
      enableResizing={panel.chrome.resizable}
      minWidth={Math.max(panel.chrome.minSize?.width ?? MIN_PANEL_WIDTH, MIN_PANEL_WIDTH)}
      minHeight={Math.max(panel.chrome.minSize?.height ?? MIN_PANEL_HEIGHT, MIN_PANEL_HEIGHT)}
      style={{ zIndex: panel.zOrder, pointerEvents: 'auto' }}
      onMouseDown={() => focusPanel(panel.id)}
      onDragStart={() => onDragStart?.()}
      // `position` is a CONTROLLED prop, so it has to track the drag as it happens: react-rnd
      // re-applies the prop value on every render, and a panel that re-renders mid-drag (focus
      // change, a live panel refreshing its own content) would otherwise snap back to the stale
      // position — which reads as the panel lagging behind the cursor instead of staying under it.
      onDrag={(_event, data) => {
        updatePosition(panel.id, { x: data.x, y: data.y })
        onDragMove?.({ x: data.x + panel.size.width / 2, y: data.y + panel.size.height / 2 })
      }}
      // specs/054 D6 — the host may snap this onto whichever landing placeholder was showing;
      // with no `onDragEnd` wired (or one that returns null/undefined), this is unchanged from
      // dropping exactly where released.
      onDragStop={(_event, data) => {
        const center = { x: data.x + panel.size.width / 2, y: data.y + panel.size.height / 2 }
        const finalPosition = onDragEnd?.(center) ?? { x: data.x, y: data.y }
        updatePosition(panel.id, finalPosition)
      }}
      onResizeStop={(_event, _direction, ref, _delta, position) => {
        updateSize(panel.id, { width: ref.offsetWidth, height: ref.offsetHeight })
        updatePosition(panel.id, position)
      }}
    >
      <Box
        role="region"
        aria-label={panel.title}
        data-density={density}
        sx={{
          position: 'relative',
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          borderRadius: compact ? '10px' : 2,
          boxShadow: 4,
          bgcolor: backgroundColor,
          color: 'text.primary',
          ...(compact && { border: '1px solid', borderColor: 'divider', backdropFilter: 'blur(6px)' }),
        }}
      >
        {panel.chrome.titleBar ? (
          <Box
            className={DRAG_HANDLE_CLASS}
            tabIndex={0}
            role="group"
            aria-label={`${panel.title} panel controls — use arrow keys to move`}
            onKeyDown={(event) => nudgePosition(event, panel, updatePosition)}
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              px: compact ? 1.25 : 1.5,
              py: compact ? 0.375 : 1,
              borderBottom: 1,
              borderColor: 'divider',
              flexShrink: 0,
              cursor: 'move',
            }}
          >
            <Typography
              variant="subtitle2"
              noWrap
              sx={{ flex: 1, minWidth: 0, ...(compact && { fontSize: 12, fontWeight: 600 }) }}
            >
              {panel.title}
            </Typography>
            <ContextAssociationControls panel={panel} />
            <IconButton
              onClick={() => minimizePanel(panel.id)}
              aria-label="Minimize panel"
              size="small"
              sx={compact ? { p: 0.375 } : undefined}
            >
              <RiSubtractLine size={controlIconSize} />
            </IconButton>
            <IconButton
              onClick={() => closePanel(panel.id)}
              aria-label="Close panel"
              size="small"
              sx={compact ? { p: 0.375 } : undefined}
            >
              <RiCloseLine size={controlIconSize} />
            </IconButton>
          </Box>
        ) : (
          // research D7 — a panel with no title bar (a compact readout or a wide control strip)
          // still needs to be movable, minimisable, closable and focusable. A small grip carries
          // the same drag-handle class and the same keyboard-nudge handler the title bar uses, so
          // both variants share one movement implementation; there is just no title text to show.
          <Box
            className={DRAG_HANDLE_CLASS}
            tabIndex={0}
            role="group"
            aria-label={`${panel.title} panel controls — use arrow keys to move`}
            onKeyDown={(event) => nudgePosition(event, panel, updatePosition)}
            sx={{
              position: 'absolute',
              top: 4,
              right: 4,
              zIndex: 1,
              display: 'flex',
              alignItems: 'center',
              gap: 0.25,
              bgcolor: 'action.selected',
              borderRadius: 4,
              px: 0.25,
              cursor: 'move',
            }}
          >
            <Box component="span" sx={{ display: 'inline-flex', px: 0.5, color: 'text.secondary' }}>
              <RiDraggable size={16} />
            </Box>
            <IconButton onClick={() => minimizePanel(panel.id)} aria-label="Minimize panel" size="small">
              <RiSubtractLine size={16} />
            </IconButton>
            <IconButton onClick={() => closePanel(panel.id)} aria-label="Close panel" size="small">
              <RiCloseLine size={16} />
            </IconButton>
          </Box>
        )}
        <Box
          sx={{
            flex: 1,
            minHeight: 0,
            overflow: 'auto',
            ...(compact ? { px: 1.75, py: 1.25 } : { p: 1.5 }),
            ...(!panel.chrome.titleBar && { pt: 4.5 }),
          }}
        >
          <PanelDensityContext.Provider value={density}>
            <PanelContent panel={panel} />
          </PanelDensityContext.Provider>
        </Box>
        {panel.chrome.resizable && (
          // Found live (2026-09-14): the resize grip used to sit absolutely positioned in the
          // corner, on top of the content area — right where its scrollbar's down arrow is, so the
          // two overlapped. The grip now lives in its own footer cell, below the scrolling area.
          // The wide left cell is a slot for hints or status; the narrow right cell fits the grip
          // alone, sitting under react-rnd's bottom-right resize handle so the glyph marks exactly
          // the spot that resizes.
          <Box
            data-testid="panel-footer"
            sx={{
              display: 'flex',
              alignItems: 'stretch',
              flexShrink: 0,
              height: compact ? 20 : 24,
              borderTop: 1,
              borderColor: 'divider',
            }}
          >
            <Box
              data-testid="panel-footer-status"
              sx={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'center', px: 1, ...(compact ? { fontSize: 11 } : { fontSize: 12 }), color: 'text.secondary' }}
            />
            <Box aria-hidden sx={{ width: '1px', my: 0.5, bgcolor: 'divider', flexShrink: 0 }} />
            <Box
              aria-hidden
              data-testid="panel-footer-resize"
              sx={{
                width: FOOTER_RESIZE_CELL_WIDTH,
                flexShrink: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'text.secondary',
                opacity: 0.6,
                pointerEvents: 'none',
              }}
            >
              <RiExpandDiagonal2Line size={14} />
            </Box>
          </Box>
        )}
      </Box>
    </Rnd>
  )
}
