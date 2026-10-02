/** What identifies a location for camera framing; the fields `activeLocationStore` holds. */
export interface FramingLocation {
  source: 'geolocation' | 'agent' | null
  latitude: number | null
  longitude: number | null
  locationType: string | null
  viewport: { northeastLat: number; northeastLng: number; southwestLat: number; southwestLng: number } | null
}

/**
 * Identifies a *deliberately* established location, as opposed to passive tracking of the one already
 * shown. The device establishes a location once and then keeps reporting it, so every geolocation fix
 * shares one key; the agent naming a place is a deliberate act every time, so its coordinates and framing
 * hints are all part of its key. `ViewerSurface` frames the camera when this changes; a camera put back
 * from memory marks its location as already framed, so the default framing does not move it again.
 */
export function framingKeyOf(location: FramingLocation): string | null {
  const { source, latitude, longitude, locationType, viewport } = location
  if (source === null) return null
  if (source === 'geolocation') return 'geolocation'

  const bounds =
    viewport === null ? '' : `${viewport.northeastLat},${viewport.northeastLng},${viewport.southwestLat},${viewport.southwestLng}`
  return `agent:${latitude},${longitude},${locationType ?? ''},${bounds}`
}
