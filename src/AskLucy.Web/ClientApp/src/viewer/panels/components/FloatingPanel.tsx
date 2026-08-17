import { RiCloseLine, RiExpandDiagonalLine, RiSubtractLine } from '@remixicon/react'
import { Box, IconButton, Typography, alpha } from '@mui/material'
import { Rnd } from 'react-rnd'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { usePanelPreferencesStore } from '../store/panelPreferencesStore'
import { MIN_PANEL_HEIGHT, MIN_PANEL_WIDTH, type FloatingPanel as FloatingPanelModel } from '../types/panel'

const DRAG_HANDLE_CLASS = 'floating-panel-drag-handle'
const MINIMIZED_BAR_WIDTH = 220
const MINIMIZED_BAR_HEIGHT = 40

export interface FloatingPanelProps {
  panel: FloatingPanelModel
}

function PanelContent({ panel }: { panel: FloatingPanelModel }) {
  const definition = panelTypeRegistry.resolve(panel.typeKey)
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

/** The chrome for a single open AI-requested panel (data-model.md "Floating Panel"). Normal
 * (non-minimized) panels are wrapped in `react-rnd` for drag/resize (FR-004/FR-005), bounded to
 * the viewer surface (`bounds="parent"`, FR-018) with a minimum usable size (Edge Cases). A
 * minimized panel renders as a small, non-draggable, non-resizable bar instead (FR-006) — simpler
 * and avoids ambiguity about what "dragging a minimized panel" should even mean, which the spec
 * never asks for. This component is intentionally namespaced under `viewer/panels/` rather than
 * reusing `components/workspace-shell/FloatingPanel.tsx`, an unrelated single-instance
 * workspace-control drawer (research.md Decision 5). */
export function FloatingPanel({ panel }: FloatingPanelProps) {
  const closePanel = useFloatingPanelStore((s) => s.closePanel)
  const focusPanel = useFloatingPanelStore((s) => s.focusPanel)
  const minimizePanel = useFloatingPanelStore((s) => s.minimizePanel)
  const restorePanel = useFloatingPanelStore((s) => s.restorePanel)
  const updatePosition = useFloatingPanelStore((s) => s.updatePosition)
  const updateSize = useFloatingPanelStore((s) => s.updateSize)
  const opacityPercent = usePanelPreferencesStore((s) => s.opacityPercent)

  const backgroundColor = (theme: { palette: { background: { paper: string } } }) =>
    alpha(theme.palette.background.paper, opacityPercent / 100)

  if (panel.minimized) {
    return (
      <Box
        role="region"
        aria-label={panel.title}
        onMouseDown={() => focusPanel(panel.id)}
        sx={{
          position: 'absolute',
          left: panel.position.x,
          top: panel.position.y,
          width: MINIMIZED_BAR_WIDTH,
          height: MINIMIZED_BAR_HEIGHT,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          px: 1,
          borderRadius: 2,
          boxShadow: 4,
          zIndex: panel.zOrder,
          bgcolor: backgroundColor,
          color: 'text.primary',
          pointerEvents: 'auto',
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
    )
  }

  return (
    <Rnd
      size={{ width: panel.size.width, height: panel.size.height }}
      position={{ x: panel.position.x, y: panel.position.y }}
      bounds="parent"
      dragHandleClassName={DRAG_HANDLE_CLASS}
      enableResizing={panel.resizable}
      minWidth={MIN_PANEL_WIDTH}
      minHeight={MIN_PANEL_HEIGHT}
      style={{ zIndex: panel.zOrder, pointerEvents: 'auto' }}
      onMouseDown={() => focusPanel(panel.id)}
      onDragStop={(_event, data) => updatePosition(panel.id, { x: data.x, y: data.y })}
      onResizeStop={(_event, _direction, ref, _delta, position) => {
        updateSize(panel.id, { width: ref.offsetWidth, height: ref.offsetHeight })
        updatePosition(panel.id, position)
      }}
    >
      <Box
        role="region"
        aria-label={panel.title}
        sx={{
          width: '100%',
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          borderRadius: 2,
          boxShadow: 4,
          bgcolor: backgroundColor,
          color: 'text.primary',
        }}
      >
        <Box
          className={DRAG_HANDLE_CLASS}
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            px: 1.5,
            py: 1,
            borderBottom: 1,
            borderColor: 'divider',
            flexShrink: 0,
            cursor: 'move',
          }}
        >
          <Typography variant="subtitle2" noWrap sx={{ flex: 1, minWidth: 0 }}>
            {panel.title}
          </Typography>
          <IconButton onClick={() => minimizePanel(panel.id)} aria-label="Minimize panel" size="small">
            <RiSubtractLine size={18} />
          </IconButton>
          <IconButton onClick={() => closePanel(panel.id)} aria-label="Close panel" size="small">
            <RiCloseLine size={18} />
          </IconButton>
        </Box>
        <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', p: 1.5 }}>
          <PanelContent panel={panel} />
        </Box>
      </Box>
    </Rnd>
  )
}
