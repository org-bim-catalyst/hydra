import { alpha, Box, Typography } from '@mui/material'
import { useState, type PointerEvent, type WheelEvent } from 'react'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { cornersInBox } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { toLocalMeters } from '../../../viewer/siteBoundaryEdit/ringGeometry'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import type { PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { usePixelProjector } from '../../../viewer/siteBoundaryEdit/usePixelProjector'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'

/** A drag shorter than this is a click, which clears the selection instead of picking anything. */
const MIN_DRAG_PX = 4

/** A press this close to a selected corner grabs the selection instead of starting a box. */
const GRAB_RADIUS_PX = 12

interface Drag {
  startX: number
  startY: number
  x: number
  y: number
}

interface Props {
  /** Test seam: the projector to use instead of one built from the live map. */
  projector?: PixelProjector | null
}

/**
 * specs/079: the Select tool. While it is chosen, this layer sits over the map and turns a drag into a
 * box; every corner of the ring being edited that falls inside the box becomes selected (Shift adds to
 * what is already selected). The map underneath gets no drag, so it cannot pan while the box is drawn;
 * the mouse wheel still zooms it. Escape, or choosing the tool again, hands the map back.
 */
export function SiteBoundaryBoxSelect({ projector: injected }: Props) {
  const map = useGoogleMapsStore((s) => s.map)
  const active = useSiteBoundaryEditStore((s) => s.session?.tool === 'select')
  const getProjector = usePixelProjector(active, injected)
  const [drag, setDrag] = useState<Drag | null>(null)
  /** While the selected corners are being dragged: where the pointer was last, on the map. */
  const [grab, setGrab] = useState<GeoPoint | null>(null)

  if (!active) return null

  const local = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  /** The map position under the pointer, and whether it is on one of the selected corners. */
  const locate = (event: PointerEvent<HTMLDivElement>) => {
    const use = getProjector()
    const session = useSiteBoundaryEditStore.getState().session
    if (!use || !session) return null
    const origin = use.origin()
    const at = { x: event.clientX - origin.left, y: event.clientY - origin.top }
    const point = use.toLatLng(at)
    const ring = session.rings[session.activeRing] ?? []
    const onSelected = session.selectedCorners.some((i) => {
      const pixel = ring[i] ? use.toPixel(ring[i]) : null
      return pixel !== null && Math.hypot(pixel.x - at.x, pixel.y - at.y) <= GRAB_RADIUS_PX
    })
    return point ? { point, onSelected } : null
  }

  const start = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return
    event.currentTarget.setPointerCapture?.(event.pointerId)

    // Pressing on a selected corner grabs the whole selection, here in the Select tool as everywhere else.
    const hit = locate(event)
    if (hit?.onSelected) {
      setGrab(hit.point)
      return
    }

    const { x, y } = local(event)
    setDrag({ startX: x, startY: y, x, y })
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (grab) {
      const hit = locate(event)
      if (!hit) return
      const [, to] = toLocalMeters([grab, hit.point], grab)
      if (to.x !== 0 || to.y !== 0) siteBoundaryEditActions.nudgeCorner(to.x, to.y)
      setGrab(hit.point)
      return
    }
    if (!drag) return
    const { x, y } = local(event)
    setDrag({ ...drag, x, y })
  }

  const finish = (event: PointerEvent<HTMLDivElement>) => {
    // Only the primary button ends a drag; a right-click release is not a selection.
    if (event.button !== 0) return
    if (grab) {
      // The selection stays selected after it was moved.
      setGrab(null)
      return
    }
    if (!drag) return
    const box = { left: drag.startX, top: drag.startY, right: drag.x, bottom: drag.y }
    setDrag(null)

    const store = useSiteBoundaryEditStore.getState()
    const session = store.session
    if (!session) return

    const tooSmall = Math.abs(drag.x - drag.startX) < MIN_DRAG_PX && Math.abs(drag.y - drag.startY) < MIN_DRAG_PX
    if (tooSmall) {
      if (!event.shiftKey) store.selectCorner(null)
      return
    }

    const use = getProjector()
    if (!use) {
      store.refuse("The map isn't ready to select corners yet - try again in a moment.")
      return
    }

    // The map element and this layer cover the same area, but map pixels are what the projection reports.
    const mapOrigin = use.origin()
    const layer = event.currentTarget.getBoundingClientRect()
    const offset = { x: layer.left - mapOrigin.left, y: layer.top - mapOrigin.top }
    const inside = cornersInBox(
      session.rings[session.activeRing] ?? [],
      (point) => {
        const pixel = use.toPixel(point)
        return pixel && { x: pixel.x - offset.x, y: pixel.y - offset.y }
      },
      box,
    )

    if (inside.length === 0) {
      store.refuse('No corners inside that box. Drag a box around the corners you want.')
      if (!event.shiftKey) store.selectCorner(null)
      return
    }

    store.selectCorners(event.shiftKey ? [...session.selectedCorners, ...inside] : inside)
  }

  // Wheel events would otherwise stop here, so zooming is forwarded to the map by hand.
  const zoom = (event: WheelEvent<HTMLDivElement>) => {
    if (!map) return
    map.setZoom((map.getZoom() ?? 17) + (event.deltaY < 0 ? 0.5 : -0.5))
  }

  const box = drag && {
    left: Math.min(drag.startX, drag.x),
    top: Math.min(drag.startY, drag.y),
    width: Math.abs(drag.x - drag.startX),
    height: Math.abs(drag.y - drag.startY),
  }

  return (
    <Box
      data-testid="corner-select-layer"
      onPointerDown={start}
      onPointerMove={move}
      onPointerUp={finish}
      onPointerCancel={() => {
        setDrag(null)
        setGrab(null)
      }}
      // The browser's own menu has no use here and would swallow the pointer-up that ends a drag; drop it.
      onContextMenu={(event) => {
        event.preventDefault()
        setDrag(null)
        setGrab(null)
        // A right-click leaves the Select tool for ordinary editing and keeps what is selected.
        useSiteBoundaryEditStore.getState().setTool('edit')
      }}
      onWheel={zoom}
      // The viewer's overlay container lets pointer events through to the map; this layer must take them
      // back, or a drag pans the map instead of drawing the box.
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none', pointerEvents: 'auto' }}
    >
      {box && (
        <Box
          data-testid="corner-select-box"
          sx={{
            position: 'absolute',
            ...box,
            border: '1.5px dashed #FFC107',
            bgcolor: alpha('#FFC107', 0.14),
            pointerEvents: 'none',
          }}
        />
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
        Drag a box around corners to select them (Shift adds). Drag a selected corner to move them all. Right-click to go back to editing.
      </Typography>
    </Box>
  )
}
