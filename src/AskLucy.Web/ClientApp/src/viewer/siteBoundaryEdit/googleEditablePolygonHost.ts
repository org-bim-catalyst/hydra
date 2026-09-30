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
  return {
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

      /** The ring of light around the selected corner. */
      let marker: google.maps.Marker | null = null

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
          const handle = polygon.addListener('click', () => listener())
          return () => handle.remove()
        },

        onVertexClick(listener) {
          // On an editable polygon Google sets `vertex` on a click that lands on a corner handle.
          const handle = polygon.addListener('click', (event: google.maps.PolyMouseEvent) => {
            if (event.vertex !== undefined && event.vertex !== null) listener(event.vertex)
          })
          return () => handle.remove()
        },

        setHighlight(index) {
          if (index === null || index >= polygon.getPath().getLength()) {
            marker?.setMap(null)
            marker = null
            return
          }
          const position = polygon.getPath().getAt(index)
          if (!marker) {
            marker = new google.maps.Marker({
              map,
              position,
              clickable: false,
              zIndex: 30,
              icon: { path: google.maps.SymbolPath.CIRCLE, scale: 9, fillColor: '#FFFFFF', fillOpacity: 0.35, strokeColor: '#FFC107', strokeWeight: 3 },
            })
          } else {
            marker.setPosition(position)
            marker.setMap(map)
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
          marker?.setMap(null)
          marker = null
          google.maps.event.clearInstanceListeners(polygon)
          polygon.setMap(null)
        },
      }
    },
  }
}
