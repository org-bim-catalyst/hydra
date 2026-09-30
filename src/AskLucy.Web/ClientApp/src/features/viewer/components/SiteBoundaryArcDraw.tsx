import { alpha, Box, Typography } from '@mui/material'
import { useState, type PointerEvent } from 'react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { arcThroughPoint } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { usePixelProjector } from '../../../viewer/siteBoundaryEdit/usePixelProjector'

interface Props {
  /** Test seam: the projector to use instead of one built from the live map. */
  projector?: PixelProjector | null
}

interface Preview {
  /** SVG `points` of the arc that would be drawn, in layer pixels; null when there is nothing to show. */
  points: string | null
  /** Why no arc can be drawn through the pointer, when that is so. */
  note: string | null
}

const DEFAULT_HINT = 'Click where the arc should pass. Press Esc to cancel.'

/**
 * specs/079: the Draw arc tool. Two corners are already chosen; the user now drops a third point on the
 * map and an arc runs through all three. While the pointer moves, the arc it would draw is shown, so
 * the point can be placed by eye. A click drops it; Esc cancels. The "Curve edge" dialog is the
 * keyboard-and-touch-friendly way to bend an edge by an exact amount.
 */
export function SiteBoundaryArcDraw({ projector: injected }: Props) {
  const anchors = useSiteBoundaryEditStore((s) => (s.session?.tool === 'arc' ? s.session.arcAnchors : null))
  const getProjector = usePixelProjector(anchors !== null, injected)
  const [preview, setPreview] = useState<Preview>({ points: null, note: null })

  if (!anchors) return null

  /** The map point under the pointer, and the conversion between layer pixels and map pixels. */
  const locate = (event: PointerEvent<HTMLDivElement>) => {
    const projector = getProjector()
    if (!projector) return null

    const rect = event.currentTarget.getBoundingClientRect()
    const origin = projector.origin()
    const offset = { x: rect.left - origin.left, y: rect.top - origin.top }
    const layerX = event.clientX - rect.left
    const layerY = event.clientY - rect.top
    const through = projector.toLatLng({ x: layerX + offset.x, y: layerY + offset.y })
    return through && { through, projector, offset }
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    const session = useSiteBoundaryEditStore.getState().session
    const hit = locate(event)
    if (!hit || !session) {
      setPreview({ points: null, note: null })
      return
    }

    const result = arcThroughPoint(session.rings[session.activeRing] ?? [], anchors[0], anchors[1], hit.through)
    if ('refusal' in result) {
      setPreview({ points: null, note: result.refusal })
      return
    }

    const pixels = result.arc.map((point) => hit.projector.toPixel(point))
    setPreview({
      points: pixels.every((pixel) => pixel !== null)
        ? pixels.map((pixel) => `${pixel!.x - hit.offset.x},${pixel!.y - hit.offset.y}`).join(' ')
        : null,
      note: null,
    })
  }

  const drop = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return
    const hit = locate(event)
    if (!hit) {
      useSiteBoundaryEditStore.getState().refuse("The map isn't ready to place an arc yet - try again in a moment.")
      return
    }
    siteBoundaryEditActions.applyArc(hit.through)
  }

  return (
    <Box
      data-testid="arc-draw-layer"
      onPointerMove={move}
      onPointerUp={drop}
      onPointerLeave={() => setPreview({ points: null, note: null })}
      // The viewer's overlay container lets pointer events through to the map; this layer takes them back.
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none', pointerEvents: 'auto' }}
    >
      {preview.points && (
        <svg width="100%" height="100%" style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }} aria-hidden>
          <polyline data-testid="arc-preview" points={preview.points} fill="none" stroke="#FFC107" strokeWidth={3} strokeDasharray="6 4" />
        </svg>
      )}
      <Typography
        role="status"
        variant="caption"
        sx={{
          position: 'absolute',
          bottom: 16,
          left: '50%',
          transform: 'translateX(-50%)',
          px: 1.5,
          py: 0.5,
          borderRadius: 2,
          pointerEvents: 'none',
          bgcolor: (t) => alpha(t.palette.background.paper, 0.92),
          color: 'text.primary',
          border: (t) => `1px solid ${t.palette.divider}`,
        }}
      >
        {preview.note ?? DEFAULT_HINT}
      </Typography>
    </Box>
  )
}
