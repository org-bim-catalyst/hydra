import * as THREE from 'three'
import { SHADOW_FRUSTUM_RATIO } from './sunLight'

/** research D16 — the ground plane's extent, derived from the SAME radius and ratio the shadow
 * camera frustum uses (`sunLight.ts`), with an extra margin so the plane sits STRICTLY inside the
 * frustum rather than merely matching it — sitting exactly at the frustum edge risks the same
 * clamped-lookup "fake grey blob" the reference implementation hit, from edge-of-frustum sampling
 * noise. The reference implementation's own proven ratio is `radius × 2.4` for a `radius × 1.3`
 * frustum half-extent (i.e. plane half-extent `radius × 1.2`, frustum half-extent `radius × 1.3`). */
export const GROUND_PLANE_RATIO = 2.4

export class ShadowGround {
  readonly mesh: THREE.Mesh
  /** The radius this plane's geometry was built for. `SolarScene.ensureShadowRig` compares against
   * it to know when the plane must be rebuilt, since `PlaneGeometry` cannot be resized in place. */
  readonly radiusMetres: number

  constructor(radiusMetres: number) {
    this.radiusMetres = radiusMetres
    const size = radiusMetres * GROUND_PLANE_RATIO
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(size, size), new THREE.ShadowMaterial({ opacity: 0.42 }))
    this.mesh.receiveShadow = true
  }

  /** FR-018, research D16 — the frustum (`radius × 1.3` half-extent) must always be larger than
   * this plane's half-extent (`radius × 1.2`). Asserted directly in `shadowGround.test.ts` rather
   * than assumed, since this is the concrete failure FR-018 guards against. */
  static frustumHalfExtentFor(radiusMetres: number): number {
    return radiusMetres * SHADOW_FRUSTUM_RATIO
  }

  groundHalfExtentFor(radiusMetres: number): number {
    return (radiusMetres * GROUND_PLANE_RATIO) / 2
  }

  /** FR-017 — hidden (no spurious ground shading) when the sun is below the horizon. */
  setVisible(isAboveHorizon: boolean): void {
    this.mesh.visible = isAboveHorizon
  }

  addTo(group: THREE.Group): void {
    group.add(this.mesh)
  }

  dispose(): void {
    this.mesh.geometry.dispose()
    ;(this.mesh.material as THREE.Material).dispose()
  }
}
