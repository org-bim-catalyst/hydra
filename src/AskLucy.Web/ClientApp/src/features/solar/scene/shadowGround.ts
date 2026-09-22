import * as THREE from 'three'
import { SHADOW_FRUSTUM_MARGIN } from './sunLight'

/**
 * research D16, FR-009 — the shadow-receiving ground plane, sized from the SAME radius the shadow
 * camera frustum is (`sunLight.ts`). The plane's half-extent is the radius itself, which is the
 * furthest a shadow tip can land; the frustum is `SHADOW_FRUSTUM_MARGIN` larger, so the plane sits
 * strictly inside it rather than exactly at its edge. Sitting at the edge risks the recorded
 * "fake grey blob": a clamped shadow-map lookup reads as in-shadow across ground nothing shades.
 *
 * The geometry is a unit square scaled to size, not a `PlaneGeometry` rebuilt per radius. Since
 * T023 the radius tracks the sun's elevation and therefore changes as the sun moves, and rebuilding
 * geometry on a time-of-day tick is exactly the cost US3 exists to remove.
 */
export class ShadowGround {
  readonly mesh: THREE.Mesh
  private currentRadiusMetres: number

  constructor(radiusMetres: number) {
    this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.ShadowMaterial({ opacity: 0.42 }))
    this.mesh.receiveShadow = true
    this.currentRadiusMetres = radiusMetres
    this.setRadius(radiusMetres)
  }

  /** The radius this plane is currently sized for. */
  get radiusMetres(): number {
    return this.currentRadiusMetres
  }

  setRadius(radiusMetres: number): void {
    this.currentRadiusMetres = radiusMetres
    const size = this.groundHalfExtentFor(radiusMetres) * 2
    this.mesh.scale.set(size, size, 1)
  }

  /** FR-009, research D16 — the frustum must always be larger than this plane's half-extent.
   * Asserted directly in `shadowGround.test.ts` rather than assumed, since this is the concrete
   * failure that requirement guards against. */
  static frustumHalfExtentFor(radiusMetres: number): number {
    return radiusMetres * SHADOW_FRUSTUM_MARGIN
  }

  groundHalfExtentFor(radiusMetres: number): number {
    return radiusMetres
  }

  /** FR-017 — hidden (no spurious ground shading) when nothing is casting. */
  setVisible(isCastingShadows: boolean): void {
    this.mesh.visible = isCastingShadows
  }

  addTo(group: THREE.Group): void {
    group.add(this.mesh)
  }

  dispose(): void {
    this.mesh.geometry.dispose()
    ;(this.mesh.material as THREE.Material).dispose()
  }
}
