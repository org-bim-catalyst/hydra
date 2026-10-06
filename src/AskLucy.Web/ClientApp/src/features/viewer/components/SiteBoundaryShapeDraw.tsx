import { alpha, Box, Typography } from '@mui/material'
import { useState, type PointerEvent } from 'react'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { toLocalMeters } from '../../../viewer/siteBoundaryEdit/ringGeometry'
import { circleRing, rectangleRing, squareRing, type ShapeResult } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { useSiteBoundaryEditStore, type ShapeKind } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { usePixelProjector } from '../../../viewer/siteBoundaryEdit/usePixelProjector'

/** A press-and-drag shorter than this is a click: no size was chosen. */
const MIN_RADIUS_METERS = 1

interface Props {
  /** Test seam: the projector to use instead of one built from the live map. */
  projector?: PixelProjector | null
}

interface Drag {
  /** Where the press was: the circle's centre, or one corner of a rectangle or square. */
  anchor: GeoPoint
  /** Where the pointer is now. */
  to: GeoPoint
  /** The shape the drag makes so far, when it is big enough to be one. */
  shape: ShapeResult | null
  /** SVG `points` of the shape being drawn, in layer pixels. */
  preview: string | null
  /** What the drag measures so far, for the hint line. */
  size: string | null
}

const VERB = { add: 'add it', cut: 'cut it out' } as const

const NOUN: Record<ShapeKind, string> = { circle: 'circle', rectangle: 'rectangle', square: 'square' }

const INSTRUCTION: Record<ShapeKind, string> = {
  circle: 'Press on the centre, drag outward to the radius',
  rectangle: 'Press on one corner, drag to the opposite corner',
  square: 'Press on one corner, drag to size the square',
}

/** The shape a drag from `anchor` to `to` draws, and what it measures. */
function shapeOf(kind: ShapeKind, anchor: GeoPoint, to: GeoPoint): { shape: ShapeResult; size: string } {
  const [from, far] = toLocalMeters([anchor, to], anchor)
  if (kind === 'circle') {
    const radius = Math.hypot(far.x - from.x, far.y - from.y)
    return { shape: circleRing(anchor, radius), size: `Radius ${Math.round(radius)} m` }
  }
  if (kind === 'square') {
    const side = Math.max(Math.abs(far.x), Math.abs(far.y))
    return { shape: squareRing(anchor, to), size: `${Math.round(side)} × ${Math.round(side)} m` }
  }
  return { shape: rectangleRing(anchor, to), size: `${Math.round(Math.abs(far.x))} × ${Math.round(Math.abs(far.y))} m` }
}

/**
 * specs/079: draws a circle on the map to add to the outline or to cut out of it. Press on the centre,
 * drag outward to set the radius (the circle follows the pointer), release to apply. The outline's geometry
 * is worked out on the server, so the result is exactly what Done will save. Esc cancels. "Make ring a
 * circle" is the typed-number way to get a circle, for the keyboard and touch.
 *
 * specs/081: the same layer draws a rectangle or a square, from one corner dragged to the opposite one. The
 * sides run east-west and north-south, as the editor's map is flat and north-up. A cut wholly inside the
 * outline makes a void.
 */
export function SiteBoundaryShapeDraw({ projector: injected }: Props) {
  const operation = useSiteBoundaryEditStore((s) => (s.session?.tool === 'circle' ? s.session.circleOperation : null))
  const kind = useSiteBoundaryEditStore((s) => s.session?.shapeKind ?? 'circle')
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
      useSiteBoundaryEditStore.getState().refuse(`The map isn't ready to draw a ${NOUN[kind]} yet - try again in a moment.`)
      return
    }

    event.currentTarget.setPointerCapture?.(event.pointerId)
    setDrag({ anchor: hit.point, to: hit.point, shape: null, preview: null, size: null })
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag) return
    const hit = locate(event)
    if (!hit) return

    const { shape, size } = shapeOf(kind, drag.anchor, hit.point)
    if (!('ring' in shape)) {
      setDrag({ ...drag, to: hit.point, shape, preview: null, size: null })
      return
    }

    const pixels = shape.ring.map((corner) => hit.projector.toPixel(corner))
    setDrag({
      ...drag,
      to: hit.point,
      shape,
      size,
      // SVG points in layer pixels; a corner the map cannot place (off the map) stops the preview, not the drag.
      preview: pixels.every((pixel) => pixel !== null)
        ? pixels.map((pixel) => `${pixel!.x - hit.offset.x},${pixel!.y - hit.offset.y}`).join(' ')
        : null,
    })
  }

  const finish = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag || event.button !== 0) return
    const { anchor, to } = drag
    setDrag(null)

    const { shape } = shapeOf(kind, anchor, to)
    if (!('ring' in shape)) {
      useSiteBoundaryEditStore.getState().refuse(
        kind === 'circle' ? "Press on the circle's centre and drag outward to set its radius." : `${INSTRUCTION[kind]}, then release.`,
      )
      return
    }

    if (kind === 'circle') {
      const [centre, edge] = toLocalMeters([anchor, to], anchor)
      void siteBoundaryEditActions.applyCircle(anchor, Math.hypot(edge.x - centre.x, edge.y - centre.y))
      return
    }
    void siteBoundaryEditActions.applyShapePolygon(shape.ring)
  }

  const colour = operation === 'add' ? '#7C4DFF' : '#FF5252'

  return (
    <Box
      data-testid="circle-draw-layer"
      onPointerDown={start}
      onPointerMove={move}
      onPointerUp={finish}
      onPointerCancel={() => setDrag(null)}
      // A press here is also the browser's own mouse press, which starts selecting page text as the pointer moves.
      onMouseDown={(event) => {
        event.preventDefault()
        window.getSelection()?.removeAllRanges()
      }}
      // The browser's own menu has no use here and would swallow the pointer-up that ends a drag; drop it.
      onContextMenu={(event) => {
        event.preventDefault()
        setDrag(null)
      }}
      // The viewer's overlay container lets pointer events through to the map; this layer takes them back.
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none', userSelect: 'none', pointerEvents: 'auto' }}
    >
      {drag?.preview && (
        <svg width="100%" height="100%" style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }} aria-hidden>
          <polygon
            data-testid="circle-preview"
            points={drag.preview}
            fill={alpha(colour, 0.18)}
            stroke={colour}
            strokeWidth={2.5}
            strokeDasharray="6 4"
          />
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
        {drag?.size && drag.shape && 'ring' in drag.shape && (kind !== 'circle' || (drag.shape.ring.length > 0 && drag.size !== 'Radius 0 m'))
          ? `${drag.size} - release to ${VERB[operation]}.`
          : `${INSTRUCTION[kind]}, release to ${VERB[operation]}. Press Esc to cancel.`}
      </Typography>
    </Box>
  )
}

export { MIN_RADIUS_METERS }
