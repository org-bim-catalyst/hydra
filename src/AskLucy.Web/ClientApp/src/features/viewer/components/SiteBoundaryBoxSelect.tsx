import { alpha, Box, Typography } from '@mui/material'
import { useEffect, useRef, useState, type PointerEvent, type WheelEvent } from 'react'
import { cornersInBox } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { createGooglePixelProjector, type PixelProjector } from '../../../viewer/siteBoundaryEdit/googlePixelProjector'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { useGoogleMapsStore } from '../../../viewer/store/googleMapsStore'

/** A drag shorter than this is a click, which clears the selection instead of picking anything. */
const MIN_DRAG_PX = 4

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
  const projectorRef = useRef<PixelProjector | null>(null)
  const [drag, setDrag] = useState<Drag | null>(null)

  // The projector is an external resource (an invisible OverlayView on the map), so it lives in a ref
  // that the effect owns: made when the Select tool turns on, released when it turns off.
  useEffect(() => {
    if (injected !== undefined || !active || !map) return

    const created = createGooglePixelProjector(map)
    projectorRef.current = created
    return () => {
      created.dispose()
      projectorRef.current = null
    }
  }, [active, map, injected])

  if (!active) return null

  const local = (event: PointerEvent<HTMLDivElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  const start = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return
    event.currentTarget.setPointerCapture?.(event.pointerId)
    const { x, y } = local(event)
    setDrag({ startX: x, startY: y, x, y })
  }

  const move = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag) return
    const { x, y } = local(event)
    setDrag({ ...drag, x, y })
  }

  const finish = (event: PointerEvent<HTMLDivElement>) => {
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

    const use = injected ?? projectorRef.current
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
      onPointerCancel={() => setDrag(null)}
      onWheel={zoom}
      sx={{ position: 'absolute', inset: 0, zIndex: 4, cursor: 'crosshair', touchAction: 'none' }}
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
        Drag a box around the corners to select them. Hold Shift to add. Press Esc when done.
      </Typography>
    </Box>
  )
}
