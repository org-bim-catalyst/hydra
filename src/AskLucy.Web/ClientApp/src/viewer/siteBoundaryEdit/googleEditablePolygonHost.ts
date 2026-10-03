import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { EditablePath, EditablePolygonHost, EditableRing } from './editablePolygonController'
import { createGooglePixelProjector } from './googlePixelProjector'

/**
 * specs/079 research D1: the real map behind {@link EditablePolygonHost}. Each ring is a native
 * `google.maps.Polygon`; while `editable`, Google draws a handle on every corner and a midpoint
 * handle on every edge and does all the dragging. This file only translates between Google's
 * `LatLng`/`MVCArray` and the plain shapes the controller works with.
 *
 * Drawn above the animated outline (zIndex 20 against its 10), which edit mode hides anyway.
 */

const ACTIVE = { stroke: '#7C4DFF', strokeOpacity: 1, fillOpacity: 0.22, strokeWeight: 3 }
const DIMMED = { stroke: '#7C4DFF', strokeOpacity: 0.4, fillOpacity: 0.08, strokeWeight: 2 }

const toLatLng = (p: GeoPoint): google.maps.LatLngLiteral => ({ lat: p.latitude, lng: p.longitude })
const fromLatLng = (p: google.maps.LatLng): GeoPoint => ({ latitude: p.lat(), longitude: p.lng() })

function adaptPath(mvc: google.maps.MVCArray<google.maps.LatLng>): EditablePath {
  const listen = (eventName: string, handler: (...args: never[]) => void) => {
    const listener = google.maps.event.addListener(mvc, eventName, handler as (...args: unknown[]) => void)
    return () => listener.remove()
  }

  return {
    getLength: () => mvc.getLength(),
    getAt: (index) => fromLatLng(mvc.getAt(index)),
    setAt: (index, point) => mvc.setAt(index, new google.maps.LatLng(toLatLng(point))),
    insertAt: (index, point) => mvc.insertAt(index, new google.maps.LatLng(toLatLng(point))),
    removeAt: (index) => {
      mvc.removeAt(index)
    },
    onSetAt: (listener) => listen('set_at', (index: number) => listener(index)),
    onInsertAt: (listener) => listen('insert_at', (index: number) => listener(index)),
    onRemoveAt: (listener) => listen('remove_at', (index: number, removed: google.maps.LatLng) => listener(index, fromLatLng(removed))),
  }
}

/**
 * The rings of light around selected corners. Drawn by an overlay of our own (plain, pointer-transparent
 * elements in the map's float pane) rather than as map markers: adding a marker while a corner is being
 * pressed rebuilds the layer Google keeps its corner handles in, which cancelled the press, so a corner had
 * to be clicked once before it could be dragged.
 */
/** The surface the rings are drawn on. */
interface CornerRingsSurface {
  show(points: Map<number, google.maps.LatLngLiteral>): void
  move(index: number, point: google.maps.LatLngLiteral): void
  setMap(map: google.maps.Map | null): void
}

/** Built only once Google's script has loaded: the overlay class extends one of its own. */
function createCornerRings(): CornerRingsSurface {
  class CornerRings extends google.maps.OverlayView {
    private readonly rings = new Map<number, { point: google.maps.LatLngLiteral; element: HTMLDivElement }>()

    /** Shows a ring at each of these corners, replacing any shown before. */
    show(points: Map<number, google.maps.LatLngLiteral>) {
      this.rings.forEach((ring) => ring.element.remove())
      this.rings.clear()
      const pane = this.getPanes()?.floatPane
      points.forEach((point, index) => {
        const element = document.createElement('div')
        element.setAttribute('aria-hidden', 'true')
        Object.assign(element.style, {
          position: 'absolute',
          width: '18px',
          height: '18px',
          marginLeft: '-9px',
          marginTop: '-9px',
          borderRadius: '50%',
          border: '3px solid #FFC107',
          background: 'rgba(255,255,255,0.35)',
          boxSizing: 'border-box',
          pointerEvents: 'none',
        })
        pane?.appendChild(element)
        this.rings.set(index, { point, element })
      })
      this.draw()
    }

    /** Moves one ring, if that corner has one. */
    move(index: number, point: google.maps.LatLngLiteral) {
      const ring = this.rings.get(index)
      if (!ring) return
      ring.point = point
      this.place(ring)
    }

    override onAdd() {
      // Rings asked for before the map was ready are attached now.
      const pane = this.getPanes()?.floatPane
      this.rings.forEach((ring) => pane?.appendChild(ring.element))
    }

    override draw() {
      this.rings.forEach((ring) => this.place(ring))
    }

    override onRemove() {
      this.rings.forEach((ring) => ring.element.remove())
    }

    private place(ring: { point: google.maps.LatLngLiteral; element: HTMLDivElement }) {
      const pixel = this.getProjection()?.fromLatLngToDivPixel(new google.maps.LatLng(ring.point))
      if (!pixel) return
      ring.element.style.left = `${pixel.x}px`
      ring.element.style.top = `${pixel.y}px`
    }
  }

  return new CornerRings()
}

/** Where the pointer last went down: Google's own mousedown event does not always carry it. */
let lastPointerDown: { clientX: number; clientY: number } | null = null
if (typeof window !== 'undefined') {
  window.addEventListener('pointerdown', (event) => (lastPointerDown = { clientX: event.clientX, clientY: event.clientY }), true)
}

export function createGoogleEditablePolygonHost(map: google.maps.Map): EditablePolygonHost {
  // Screen position to map position, for following a handle while Google drags it.
  const projector = createGooglePixelProjector(map)
  return {
    onEmptyClick(listener) {
      // The map's own click and right-click fire only off the polygons; a right-click on a ring away from
      // its corners is reported by the ring itself, and a left one by the ring's onSelect.
      // A right-click does not end a selection; only a left click does.
      const click = map.addListener('click', () => listener())
      return () => click.remove()
    },
    createRing(corners, { editable }): EditableRing {
      const style = editable ? ACTIVE : DIMMED
      const polygon = new google.maps.Polygon({
        map,
        paths: corners.map(toLatLng),
        editable,
        draggable: false,
        clickable: true,
        geodesic: false,
        strokeColor: style.stroke,
        strokeOpacity: style.strokeOpacity,
        strokeWeight: style.strokeWeight,
        fillColor: style.stroke,
        fillOpacity: style.fillOpacity,
        zIndex: 20,
      })

      /** A ring of light around each selected corner. */
      const rings = createCornerRings()
      rings.setMap(map)
      // A ring follows its corner when it is dropped, moved by the keyboard, or carried along with a selected
      // group: the path reports every one of those as a set_at.
      const followCorner = google.maps.event.addListener(polygon.getPath(), 'set_at', (index: number) => {
        rings.move(index, toLatLng(fromLatLng(polygon.getPath().getAt(index))))
      })

      return {
        path: adaptPath(polygon.getPath()),

        setEditable(nowEditable) {
          const next = nowEditable ? ACTIVE : DIMMED
          polygon.setOptions({
            editable: nowEditable,
            strokeOpacity: next.strokeOpacity,
            strokeWeight: next.strokeWeight,
            fillOpacity: next.fillOpacity,
          })
        },

        onSelect(listener) {
          // Only a click away from the corners: a click on a corner is a vertex click.
          const handle = polygon.addListener('click', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex !== undefined && event.vertex !== null) return
            listener()
          })
          return () => handle.remove()
        },

        onVertexClick(listener) {
          // On an editable polygon Google sets `vertex` on a click that lands on a corner handle.
          const handle = polygon.addListener('click', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex === undefined || event.vertex === null) return
            const dom = event.domEvent as MouseEvent | undefined
            listener(event.vertex, Boolean(dom?.shiftKey || dom?.ctrlKey || dom?.metaKey))
          })
          return () => handle.remove()
        },

        setHighlights(indices) {
          const path = polygon.getPath()
          rings.show(
            new Map(
              indices
                .filter((index) => index < path.getLength())
                .map((index) => [index, toLatLng(fromLatLng(path.getAt(index)))] as const),
            ),
          )
        },

        onVertexPress(listener) {
          const handle = polygon.addListener('mousedown', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex !== undefined && event.vertex !== null) listener(event.vertex)
          })
          return () => handle.remove()
        },

        onVertexDragMove(listener) {
          // Google says which corner was pressed ('mousedown' with `vertex`) and then reports nothing until
          // the drop, so the pointer is followed on the page until it is released.
          let stop: (() => void) | null = null
          const down = polygon.addListener('mousedown', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex === undefined || event.vertex === null) return
            const index = event.vertex
            stop?.()
            // The pointer seldom lands on the corner's exact centre, and Google keeps that offset while it drags
            // the handle. So the corner is where it was when pressed, plus how far the pointer has moved since:
            // the ring stays centred on the handle instead of on the pointer.
            const corner = fromLatLng(polygon.getPath().getAt(index))
            const pointAt = (clientX: number, clientY: number) => {
              const origin = projector.origin()
              return projector.toLatLng({ x: clientX - origin.left, y: clientY - origin.top })
            }
            // Where the press was, from the page's own pointerdown (Google's event may carry no position);
            // failing that, the first movement, which is at most a pixel or two later.
            const dom = event.domEvent as MouseEvent | undefined
            const press = typeof dom?.clientX === 'number' ? dom : lastPointerDown
            let pressedAt = press ? pointAt(press.clientX, press.clientY) : null
            const move = (e: PointerEvent) => {
              const now = pointAt(e.clientX, e.clientY)
              if (!now) return
              pressedAt ??= now
              const point = {
                latitude: corner.latitude + (now.latitude - pressedAt.latitude),
                longitude: corner.longitude + (now.longitude - pressedAt.longitude),
              }
              rings.move(index, toLatLng(point))
              listener(index, point)
            }
            const up = () => stop?.()
            window.addEventListener('pointermove', move, true)
            window.addEventListener('pointerup', up, true)
            stop = () => {
              window.removeEventListener('pointermove', move, true)
              window.removeEventListener('pointerup', up, true)
              stop = null
            }
          })
          return () => {
            stop?.()
            down.remove()
          }
        },

        onVertexMenu(listener) {
          // Google reports `vertex` only for a polygon that is editable, and only when the pointer
          // is on a corner handle; a touch long-press arrives as the same 'contextmenu'.
          const handle = polygon.addListener('contextmenu', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex === undefined || event.vertex === null) return
            const dom = event.domEvent as MouseEvent | undefined
            listener(event.vertex, dom?.clientX ?? 0, dom?.clientY ?? 0)
          })
          return () => handle.remove()
        },

        remove() {
          followCorner.remove()
          rings.setMap(null)
          google.maps.event.clearInstanceListeners(polygon)
          polygon.setMap(null)
        },
      }
    },
  }
}
