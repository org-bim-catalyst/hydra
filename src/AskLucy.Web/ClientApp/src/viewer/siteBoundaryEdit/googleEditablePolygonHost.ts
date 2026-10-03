import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { EditablePath, EditablePolygonHost, EditableRing } from './editablePolygonController'

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

export function createGoogleEditablePolygonHost(map: google.maps.Map): EditablePolygonHost {
  const emptyListeners = new Set<() => void>()
  return {
    onEmptyClick(listener) {
      // The map's own click and right-click fire only off the polygons; a right-click on a ring away from
      // its corners is reported by the ring itself, and a left one by the ring's onSelect.
      emptyListeners.add(listener)
      const click = map.addListener('click', () => listener())
      const rightClick = map.addListener('contextmenu', () => listener())
      return () => {
        emptyListeners.delete(listener)
        click.remove()
        rightClick.remove()
      }
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

      /** A ring of light around each selected corner, keyed by corner index. */
      let markers = new Map<number, google.maps.Marker>()
      // The light follows its corner while it is dragged, moved by the keyboard, or carried along with a
      // selected group: the path reports every one of those as a set_at.
      // A right-click on the ring away from its corners counts as an empty click.
      polygon.addListener('contextmenu', (event: google.maps.PolyMouseEvent) => {
        if (event.vertex === undefined || event.vertex === null) emptyListeners.forEach((l) => l())
      })
      const followCorner = google.maps.event.addListener(polygon.getPath(), 'set_at', (index: number) => {
        markers.get(index)?.setPosition(polygon.getPath().getAt(index))
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
          markers.forEach((m) => m.setMap(null))
          const path = polygon.getPath()
          markers = new Map(
            indices
              .filter((index) => index < path.getLength())
              .map((index) => [
                index,
                new google.maps.Marker({
                  map,
                  position: path.getAt(index),
                  clickable: false,
                  zIndex: 30,
                  icon: { path: google.maps.SymbolPath.CIRCLE, scale: 9, fillColor: '#FFFFFF', fillOpacity: 0.35, strokeColor: '#FFC107', strokeWeight: 3 },
                }),
              ]),
          )
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
          markers.forEach((m) => m.setMap(null))
          markers = new Map()
          google.maps.event.clearInstanceListeners(polygon)
          polygon.setMap(null)
        },
      }
    },
  }
}
