import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { CameraViewMode } from '../api/commands'
import { CAMERA_VIEW_MODE_TILT } from '../camera/cameraViewMode'
import type { ViewState } from './siteBoundaryEditStore'

/**
 * specs/079 research D2: outline edit mode takes the view over (top-down, north-up, still) and must
 * hand it back exactly as it found it. Kept behind small interfaces so the sequence, which is what
 * matters, is testable without a real Google map.
 */

/** The part of `google.maps.Map` this needs. */
export interface EditableViewMap {
  getCenter(): { lat(): number; lng(): number } | null | undefined
  getZoom(): number | null | undefined
  getHeading(): number | null | undefined
  getTilt(): number | null | undefined
  moveCamera(options: { center?: { lat: number; lng: number }; zoom?: number; heading?: number; tilt?: number }): void
  fitBounds(bounds: { north: number; south: number; east: number; west: number }, padding: number): void
}

/** The viewer engine's own camera commands, so the camera store and the render target stay in step. */
export interface EditableViewEngine {
  setViewMode(mode: CameraViewMode): unknown
  setRotationEnabled(enabled: boolean): unknown
}

export interface ViewStateDeps {
  map: EditableViewMap
  engine: EditableViewEngine
  /** The camera store's current mode and rotation flag. */
  camera(): { mode: CameraViewMode; rotationEnabled: boolean }
}

/** Space kept clear around the outline when framing it, in pixels, so corner handles are never at the edge. */
export const FRAME_PADDING_PX = 48

/** Everything needed to put the view back exactly as it is right now. */
export function captureViewState(deps: ViewStateDeps): ViewState {
  const { map } = deps
  const center = map.getCenter()
  const { mode, rotationEnabled } = deps.camera()

  return {
    mode,
    rotationEnabled,
    center: { latitude: center?.lat() ?? 0, longitude: center?.lng() ?? 0 },
    zoom: map.getZoom() ?? 0,
    heading: map.getHeading() ?? 0,
    tilt: map.getTilt() ?? CAMERA_VIEW_MODE_TILT[mode],
  }
}

function boundsOf(rings: readonly (readonly GeoPoint[])[]): { north: number; south: number; east: number; west: number } | null {
  const points = rings.flat()
  if (points.length === 0) return null

  return {
    north: Math.max(...points.map((p) => p.latitude)),
    south: Math.min(...points.map((p) => p.latitude)),
    east: Math.max(...points.map((p) => p.longitude)),
    west: Math.min(...points.map((p) => p.longitude)),
  }
}

/**
 * Top-down, facing north, still, and framed on the whole outline. Rotation stops first: a rotating
 * map would carry the corner handles out from under the pointer. Mode next, so the plan tilt is the
 * one in force when the heading is set, then the frame.
 */
export function enterPlanForEditing(deps: ViewStateDeps, rings: readonly (readonly GeoPoint[])[]): void {
  deps.engine.setRotationEnabled(false)
  deps.engine.setViewMode('plan')
  deps.map.moveCamera({ heading: 0, tilt: CAMERA_VIEW_MODE_TILT.plan })

  const bounds = boundsOf(rings)
  if (bounds) deps.map.fitBounds(bounds, FRAME_PADDING_PX)
}

/**
 * The reverse, in the order that keeps the view stable: the mode first (so its tilt is the one the
 * map holds), then the exact camera, and rotation last, so the rotation driver picks up from the
 * restored heading instead of fighting it (research D2).
 */
export function restoreViewState(deps: ViewStateDeps, state: ViewState): void {
  deps.engine.setViewMode(state.mode)
  deps.map.moveCamera({
    center: { lat: state.center.latitude, lng: state.center.longitude },
    zoom: state.zoom,
    heading: state.heading,
    tilt: state.tilt,
  })
  deps.engine.setRotationEnabled(state.rotationEnabled)
}
