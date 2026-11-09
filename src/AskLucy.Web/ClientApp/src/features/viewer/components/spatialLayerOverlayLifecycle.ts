/** Detach every imperative map overlay owned by the spatial layer renderer. */
export function disposeSpatialLayerOverlays(
  markers: google.maps.marker.AdvancedMarkerElement[],
  polygons: google.maps.Polygon[],
  polylines: google.maps.Polyline[],
) {
  markers.forEach((marker) => { marker.map = null })
  polygons.forEach((polygon) => { polygon.setMap(null) })
  polylines.forEach((polyline) => { polyline.setMap(null) })
}
