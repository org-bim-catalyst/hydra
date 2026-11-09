import type { SpatialQueryFeature } from '../../../store/spatialLayersStore'

export type SpatialMapFeatureGeometry =
  | { kind: 'point'; points: [{ lat: number; lng: number }] }
  | { kind: 'polygon'; points: Array<{ lat: number; lng: number }> }
  | { kind: 'linestring'; points: Array<{ lat: number; lng: number }> }

/** Converts the normalized WGS84 feature into the primitive coordinates used by the map renderer. */
export function toSpatialMapFeatureGeometry(feature: SpatialQueryFeature): SpatialMapFeatureGeometry | null {
  const points = feature.geometry.points.map((point) => ({ lat: point.latitude, lng: point.longitude }))
  if (feature.geometry.kind === 'point' && points.length === 1) {
    return { kind: 'point', points: [points[0]] }
  }
  if (feature.geometry.kind === 'polygon' && points.length >= 4 &&
      points[0].lat === points.at(-1)?.lat && points[0].lng === points.at(-1)?.lng) {
    return { kind: 'polygon', points }
  }
  if (feature.geometry.kind === 'linestring' && points.length >= 2) {
    return { kind: 'linestring', points }
  }
  return null
}
