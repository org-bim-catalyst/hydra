import type * as THREE from 'three'

/** Copies the map transformer's matrix onto a viewer camera and resets its pose to the identity
 * the map's local frame assumes.
 *
 * Google's `WebGLOverlayView` hands `onDraw` a matrix from
 * `transformer.fromLatLngAltitude({ lat, lng, altitude: 0 })` that already folds the map's
 * centre, zoom, heading, tilt and viewport aspect into one projection. The overlay's scene is
 * drawn around that reference point, so the camera itself must sit at the origin with identity
 * rotation: its world matrix is then the identity and the projection matrix alone maps local
 * coordinates into clip space. Both renderers use this one function so their cameras cannot drift
 * apart (specs/087 FR-005).
 */
export function applyMapTransformToCamera(camera: THREE.Camera, matrix: ArrayLike<number>): void {
  camera.projectionMatrix.fromArray(matrix)
  camera.projectionMatrixInverse.copy(camera.projectionMatrix).invert()
  camera.position.set(0, 0, 0)
  camera.quaternion.identity()
  camera.scale.set(1, 1, 1)
  // Recomputes matrixWorld and, for a Camera, matrixWorldInverse — the renderer reads the latter.
  camera.updateMatrixWorld(true)
}
