import type { CameraViewMode } from '../api/commands'

/** FR-013 (as revised): isometric is the default angled, three-dimensional perspective; plan
 * is a top-down, orthographic-style perspective suited to reading map/GIS content (spec.md
 * Assumptions). Expressed as a map tilt in degrees — 0 is looking straight down. */
export const CAMERA_VIEW_MODE_TILT: Record<CameraViewMode, number> = {
  isometric: 45,
  plan: 0,
}

export interface TiltableTarget {
  setTilt(tilt: number): void
}

/** Applies a camera view mode to whichever real render target is active. Never called for the
 * placeholder (FR-004/FR-013/FR-017) — only `MapRenderTarget` registers a target handle. */
export function applyCameraViewMode(target: TiltableTarget, mode: CameraViewMode): void {
  target.setTilt(CAMERA_VIEW_MODE_TILT[mode])
}
