import { Box } from '@mui/material'
import { useEffect, useRef } from 'react'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { FloatingPanel } from './FloatingPanel'

/** Mounted once over the viewer (`features/viewer/components/ViewerSurface.tsx`, FR-002). Renders
 * every open `floatingPanelStore` panel — `FloatingPanel` itself decides how to present a
 * minimized panel (a compact bar) vs. a normal one (full `react-rnd` chrome), so this host only
 * owns layout, not panel state. The host lets pointer events pass through to the viewer
 * everywhere except where an individual panel actually sits, so the viewer stays fully
 * interactive while any number of panels are open (FR-003) — mirroring how `WorkspaceOverlay`
 * layers over `ViewerSurface` in spec 027. It also re-clamps every panel's position back within
 * the current viewport on resize (FR-018, Edge Cases: viewport resize). */
export function FloatingPanelHost() {
  const panels = useFloatingPanelStore((s) => s.panels)
  const clampToViewport = useFloatingPanelStore((s) => s.clampToViewport)
  const hostRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handleResize = () => {
      const bounds = hostRef.current?.getBoundingClientRect()
      if (bounds) {
        clampToViewport({ width: bounds.width, height: bounds.height })
      }
    }
    window.addEventListener('resize', handleResize)
    return () => window.removeEventListener('resize', handleResize)
  }, [clampToViewport])

  return (
    <Box ref={hostRef} sx={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }}>
      {panels.map((panel) => (
        <FloatingPanel key={panel.id} panel={panel} />
      ))}
    </Box>
  )
}
