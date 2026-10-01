import { alpha, Box, Typography } from '@mui/material'
import { useState, type PointerEvent } from 'react'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { toLocalMeters } from '../../../viewer/siteBoundaryEdit/ringGeometry'
import { circleRing } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { usePixelProjector } from '../../../viewer/siteBoundaryEdit/usePixelProjector'

/** A press-and-drag shorter than this is a click: no radius was chosen. */
const MIN_RADIUS_METERS = 1

interface Props {
  /** Test seam: the projector to use instead of one built from the live map. */
  projector?: PixelProjector | null
}

interface Drag {
  centre: GeoPoint
  radiusMeters: number
  /** SVG `points` of the circle being drawn, in layer pixels. */
  preview: string | null
}

const VERB = { add: 'add it', cut: 'cut it out' } as const

/**
 * specs/079: draws a circle on the map to add to the outline or to cut out of it. Press on the centre,
 * drag outward to set the radius (the circle follows the pointer), release to apply. The outline's geometry
 * is worked out on the server, so the result is exactly what Done will save. Esc cancels. "Make ring a
 * circle" is the typed-number way to get a circle, for the keyboard and touch.
 */
export function SiteBoundaryCircleDraw({ projector: injected }: Props) {
  const operation = useSiteBoundaryEditStore((s) => (s.session?.tool === 'circle' ? s.session.circleOperation : null))
  const getProjector = usePixelProjector(operation !== null, injected)
  const [drag, setDrag] = useState<Drag | null>(null)

  if (!operation) return null

  /** The map point under the pointer, with the conversion between layer pixels and map pixels. */
  const locate = (event: PointerEvent<HTMLDivElement>) => {
    const projector = getProjector()
    if (!projector) return null

    const rect = event.currentTarget.getBoundingClientRect()
    const origin = projector.origin()
    const offset = { x: rect.left - origin.left, y: rect.top - origin.top }
    const point = projector.toLatLng({ x: event.clientX - rect.left + offset.x, y: event.clientY - rect.top + offset.y })
    return point && { point, projector, offset }
  }

  const start = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return
    const hit = locate(event)
    if (!hit) {
      useSiteBoundaryEditStore.getState().refuse("The map isn't ready to draw a circle yet - try again in a moment.")
      return
    }

    event.currentTarget.setPointerCapture?.(event.pointerId)
    setDrag({ centre: hit.point, radiusMeters: 0, preview: null })
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag) return
    const hit = locate(event)
    if (!hit) return

    const [centre, edge] = toLocalMeters([drag.centre, hit.point], drag.centre)
    const radiusMeters = Math.hypot(edge.x - centre.x, edge.y - centre.y)
    if (radiusMeters < MIN_RADIUS_METERS) {
      setDrag({ ...drag, radiusMeters, preview: null })
      return
    }

    const circle = circleRing(drag.centre, radiusMeters)
    const pixels = 'ring' in circle ? circle.ring.map((corner) => hit.projector.toPixel(corner)) : []
    setDrag({
      ...drag,
      radiusMeters,
      preview: pixels.length > 0 && pixels.every((pixel) => pixel !== null)
        ? pixels.map((pixel) => `${pixel!.x - hit.offset.x},${pixel!.y - hit.offset.y}`).join(' ')
        : null,
    })
  }

  const finish = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag || event.button !== 0) return
    const { centre, radiusMeters } = drag
    setDrag(null)

    if (radiusMeters < MIN_RADIUS_METERS) {
      useSiteBoundaryEditStore.getState().refuse('Press on the circle\'s centre and drag outward to set its radius.')
      return
    }
    void siteBoundaryEditActions.applyCircle(centre, radiusMeters)
  }

  const colour = operation === 'add' ? '#7C4DFF' : '#FF5252'

  return (
    <Box
      data-testid="circle-draw-layer"
      onPointerDown={start}
      onPointerMove={move}
      onPointerUp={finish}
      onPointerCancel={() => setDrag(null)}
      // The browser's own menu has no use here and would swallow the pointer-up that ends a drag; drop it.
      onContextMenu={(event) => {
        event.preventDefault()
        setDrag(null)
      }}
      // The viewer's overlay container lets pointer events through to the map; this layer takes them back.
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none', pointerEvents: 'auto' }}
    >
      {drag?.preview && (
        <svg width="100%" height="100%" style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }} aria-hidden>
          <polygon data-testid="circle-preview" points={drag.preview} fill={alpha(colour, 0.18)} stroke={colour} strokeWidth={2.5} strokeDasharray="6 4" />
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
        {drag && drag.radiusMeters >= MIN_RADIUS_METERS
          ? `Radius ${Math.round(drag.radiusMeters)} m - release to ${VERB[operation]}.`
          : `Press on the centre, drag outward to the radius, release to ${VERB[operation]}. Press Esc to cancel.`}
      </Typography>
    </Box>
  )
}
