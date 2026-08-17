import { Box } from '@mui/material'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { FloatingPanel } from './FloatingPanel'

/** Mounted once over the viewer (`features/viewer/components/ViewerSurface.tsx`, FR-002). Renders
 * every open `floatingPanelStore` panel. The host itself lets pointer events pass through to the
 * viewer everywhere except where an individual panel actually sits, so the viewer stays fully
 * interactive while any number of panels are open (FR-003) — mirroring how `WorkspaceOverlay`
 * layers over `ViewerSurface` in spec 027. */
export function FloatingPanelHost() {
  const panels = useFloatingPanelStore((s) => s.panels)

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }}>
      {panels.map((panel) =>
        panel.minimized ? null : <FloatingPanel key={panel.id} panel={panel} />,
      )}
    </Box>
  )
}
