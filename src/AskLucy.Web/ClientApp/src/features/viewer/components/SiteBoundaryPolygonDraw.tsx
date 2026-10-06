import { alpha, Box, Typography } from '@mui/material'
import { useEffect, useRef, useState, type MouseEvent, type PointerEvent } from 'react'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { validateRing } from '../../../viewer/siteBoundaryEdit/ringGeometry'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { usePixelProjector } from '../../../viewer/siteBoundaryEdit/usePixelProjector'

/** A click this close (in pixels) to the first corner closes the polygon. */
const CLOSE_RADIUS_PX = 10

interface Props {
  /** Test seam: the projector to use instead of one built from the live map. */
  projector?: PixelProjector | null
}

const VERB = { add: 'add it', cut: 'cut it out' } as const

/**
 * specs/081: draws a free polygon on the map, corner by corner, to add to the outline or to cut out of it.
 * Click to place corners; finish with a double-click, a click on the first corner, or Enter. Backspace takes
 * the last corner back, Esc cancels the whole tool. For the keyboard, Space places a corner at the middle of
 * the map (under the crosshair); a tap places one on touch. A polygon that crosses itself is refused before
 * it is sent, with the reason.
 */
export function SiteBoundaryPolygonDraw({ projector }: Props) {
  const operation = useSiteBoundaryEditStore((s) => (s.session?.tool === 'polygon' ? s.session.polygonOperation : null))
  // Keyed by the operation, so a polygon starts empty whenever the tool is restarted as the other operation.
  return operation ? <PolygonLayer key={operation} operation={operation} injected={projector} /> : null
}

/** A corner placed: where it is on the map, and where it was drawn on the layer. */
interface Corner {
  point: GeoPoint
  pixel: { x: number; y: number }
}

function PolygonLayer({ operation, injected }: { operation: 'add' | 'cut'; injected?: PixelProjector | null }) {
  const getProjector = usePixelProjector(true, injected)
  const [placed, setPlaced] = useState<Corner[]>([])
  const [pointer, setPointer] = useState<{ x: number; y: number } | null>(null)
  const layer = useRef<HTMLDivElement>(null)
  const corners = placed.map((corner) => corner.point)

  const finish = (points: GeoPoint[]) => {
    const refusal = validateRing(points)
    if (refusal) {
      useSiteBoundaryEditStore.getState().refuse(refusal.message)
      return
    }
    setPlaced([])
    void siteBoundaryEditActions.applyShapePolygon(points)
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Enter' && corners.length > 0) {
        event.preventDefault()
        finish(corners)
      } else if (event.key === 'Backspace' && corners.length > 0) {
        event.preventDefault()
        setPlaced((current) => current.slice(0, -1))
      } else if (event.key === ' ' || event.code === 'Space') {
        const rect = layer.current?.getBoundingClientRect()
        const projector = getProjector()
        if (!rect || !projector) return
        const origin = projector.origin()
        const local = { x: rect.width / 2, y: rect.height / 2 }
        const point = projector.toLatLng({ x: rect.left - origin.left + local.x, y: rect.top - origin.top + local.y })
        if (!point) return
        event.preventDefault()
        setPlaced((current) => [...current, { point, pixel: local }])
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
    // `finish` only reads the store and `corners`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [corners])

  const locate = (event: { clientX: number; clientY: number }) => {
    const projector = getProjector()
    const rect = layer.current?.getBoundingClientRect()
    if (!projector || !rect) return null
    const origin = projector.origin()
    const offset = { x: rect.left - origin.left, y: rect.top - origin.top }
    const local = { x: event.clientX - rect.left, y: event.clientY - rect.top }
    const point = projector.toLatLng({ x: local.x + offset.x, y: local.y + offset.y })
    return point && { point, projector, offset, local }
  }

  const place = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return
    const hit = locate(event)
    if (!hit) {
      useSiteBoundaryEditStore.getState().refuse("The map isn't ready to draw a shape yet - try again in a moment.")
      return
    }

    // A click back on the first corner closes the polygon.
    if (corners.length >= 3) {
      const first = hit.projector.toPixel(corners[0])
      if (first && Math.hypot(first.x - hit.offset.x - hit.local.x, first.y - hit.offset.y - hit.local.y) <= CLOSE_RADIUS_PX) {
        finish(corners)
        return
      }
    }
    setPlaced((current) => [...current, { point: hit.point, pixel: hit.local }])
  }

  const track = (event: PointerEvent<HTMLDivElement>) => {
    const hit = locate(event)
    if (hit) setPointer(hit.local)
  }

  const doubleClick = (event: MouseEvent<HTMLDivElement>) => {
    event.preventDefault()
    // The two clicks of the double-click each placed a corner; the second sits on the first, so drop it.
    const points = corners.length > 1 ? corners.slice(0, -1) : corners
    finish(points)
  }

  const colour = operation === 'add' ? '#7C4DFF' : '#FF5252'
  const visible = placed.map((corner) => corner.pixel)
  const line = [...visible, ...(pointer ? [pointer] : [])].map((pixel) => `${pixel.x},${pixel.y}`).join(' ')

  return (
    <Box
      ref={layer}
      data-testid="polygon-draw-layer"
      onPointerDown={place}
      onPointerMove={track}
      onDoubleClick={doubleClick}
      onMouseDown={(event) => {
        event.preventDefault()
        window.getSelection()?.removeAllRanges()
      }}
      onContextMenu={(event) => {
        event.preventDefault()
        setPlaced((current) => current.slice(0, -1))
      }}
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none', userSelect: 'none', pointerEvents: 'auto' }}
    >
      <svg width="100%" height="100%" style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }} aria-hidden>
        {visible.length > 0 && (
          <polygon data-testid="polygon-preview" points={line} fill={alpha(colour, 0.18)} stroke={colour} strokeWidth={2.5} strokeDasharray="6 4" />
        )}
        {visible.map((pixel, index) => (
          <circle key={index} cx={pixel.x} cy={pixel.y} r={index === 0 ? 6 : 4} fill={colour} stroke="#fff" strokeWidth={1.5} />
        ))}
      </svg>
      {/* The crosshair for the keyboard: Space places a corner here. */}
      <Box
        aria-hidden
        sx={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          width: '12px',
          height: '12px',
          ml: '-6px',
          mt: '-6px',
          border: `1px solid ${colour}`,
          borderRadius: '50%',
          pointerEvents: 'none',
        }}
      />
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
        {corners.length === 0
          ? `Click to place the first corner (or press Space at the crosshair). Press Esc to cancel.`
          : `${corners.length} corner${corners.length === 1 ? '' : 's'} - ${
              corners.length < 3 ? 'place at least 3' : `double-click, click the first corner or press Enter to ${VERB[operation]}`
            }. Backspace undoes the last corner.`}
      </Typography>
    </Box>
  )
}
