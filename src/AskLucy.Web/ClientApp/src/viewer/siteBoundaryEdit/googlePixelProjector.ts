import type { GeoPoint } from '../../store/activeSiteBoundaryStore'

/**
 * specs/079: where a corner is on the screen, so a box drawn on the screen can pick the corners inside
 * it. Google only hands out its projection through an `OverlayView`, so a do-nothing one is attached
 * to the map for as long as the projector lives. Pixels are relative to the map's own element, the
 * same frame `fromLatLngToContainerPixel` uses.
 */
export interface PixelProjector {
  /** The corner's pixel position, or null while the map cannot place it (not ready, off the map). */
  toPixel(point: GeoPoint): { x: number; y: number } | null
  /** The point on the map under a pixel, or null while the map cannot say. */
  toLatLng(pixel: { x: number; y: number }): GeoPoint | null
  /** The map element's position on the page, to turn pointer coordinates into map pixels. */
  origin(): { left: number; top: number }
  dispose(): void
}

export function createGooglePixelProjector(map: google.maps.Map): PixelProjector {
  const overlay = new google.maps.OverlayView()
  overlay.onAdd = () => {}
  overlay.draw = () => {}
  overlay.onRemove = () => {}
  overlay.setMap(map)

  return {
    toPixel(point) {
      const projection = overlay.getProjection()
      if (!projection) return null
      const pixel = projection.fromLatLngToContainerPixel(new google.maps.LatLng(point.latitude, point.longitude))
      return pixel ? { x: pixel.x, y: pixel.y } : null
    },

    toLatLng(pixel) {
      const projection = overlay.getProjection()
      if (!projection) return null
      const latLng = projection.fromContainerPixelToLatLng(new google.maps.Point(pixel.x, pixel.y))
      return latLng ? { latitude: latLng.lat(), longitude: latLng.lng() } : null
    },

    origin() {
      const rect = map.getDiv().getBoundingClientRect()
      return { left: rect.left, top: rect.top }
    },

    dispose() {
      overlay.setMap(null)
    },
  }
}
