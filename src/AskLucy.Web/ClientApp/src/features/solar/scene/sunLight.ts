import * as THREE from 'three'
import { solarPositionToEnuUnitVector } from '../solar/solarPosition'

/** research D16 — the shadow camera frustum and the ShadowMaterial ground plane (shadowGround.ts)
 * must be derived from ONE radius value, with the ground plane strictly inside the frustum, or a
 * clamped shadow-map lookup produces a large fake grey blob unrelated to real geometry — the
 * concrete failure FR-018 is written against. */
export const SHADOW_FRUSTUM_RATIO = 1.3
export const SUN_LIGHT_DISTANCE_METRES = 400

/**
 * contracts/solar-extension.md, research D6, D16 — a `DirectionalLight` + `AmbientLight` pair
 * built once per activation, sized to the analysis radius. Never touches
 * `renderer.shadowMap.enabled` or `.type` — those are the renderer-global settings FR-038 exists
 * to keep out of a capability's hands; shadows are turned on only by declaring the `shadows`
 * drawing requirement (T044), and this module leaves the shadow-map *algorithm* (`PCFSoftShadowMap`
 * in the reference implementation) at whatever the renderer's own default already is, since
 * setting it here would be exactly the class of violation FR-038 forbids.
 */
export class SunLight {
  readonly directionalLight: THREE.DirectionalLight
  readonly ambientLight: THREE.AmbientLight

  constructor(radiusMetres: number, tallestBuildingMetres: number = 50) {
    this.directionalLight = new THREE.DirectionalLight(0xfff2d0, 2.6)
    this.directionalLight.castShadow = true
    this.directionalLight.shadow.mapSize.set(2048, 2048)
    this.directionalLight.shadow.bias = -0.0006

    this.ambientLight = new THREE.AmbientLight(0xffffff, 0.7)

    this.configureShadowCamera(radiusMetres, tallestBuildingMetres)
  }

  /** research D16 — the orthographic shadow camera's extent is `±(radius × 1.3)`, the same ratio
   * `shadowGround.ts`'s ground plane extent is derived from, so the two can never drift apart
   * independently. `near`/`far` bracket the tallest building known so far. */
  configureShadowCamera(radiusMetres: number, tallestBuildingMetres: number): void {
    const extent = radiusMetres * SHADOW_FRUSTUM_RATIO
    const camera = this.directionalLight.shadow.camera as THREE.OrthographicCamera
    camera.left = -extent
    camera.right = extent
    camera.top = extent
    camera.bottom = -extent
    camera.near = Math.max(1, SUN_LIGHT_DISTANCE_METRES - tallestBuildingMetres - 50)
    camera.far = SUN_LIGHT_DISTANCE_METRES + tallestBuildingMetres + 50
    camera.updateProjectionMatrix()
  }

  /** FR-016, FR-017 — aims the light along the sun's ENU direction at the given azimuth/altitude,
   * and disables it (no shadows cast, evident to the user) when the sun is below the horizon. */
  aimAt(azimuthDegrees: number, altitudeDegrees: number): void {
    const isAboveHorizon = altitudeDegrees > 0
    this.directionalLight.visible = isAboveHorizon
    this.directionalLight.intensity = isAboveHorizon ? 2.6 : 0

    const direction = solarPositionToEnuUnitVector(azimuthDegrees, altitudeDegrees)
    this.directionalLight.position.set(
      direction.x * SUN_LIGHT_DISTANCE_METRES,
      direction.y * SUN_LIGHT_DISTANCE_METRES,
      direction.z * SUN_LIGHT_DISTANCE_METRES,
    )
    this.directionalLight.target.position.set(0, 0, 0)
    this.directionalLight.target.updateMatrixWorld()
  }

  addTo(group: THREE.Group): void {
    group.add(this.directionalLight, this.directionalLight.target, this.ambientLight)
  }

  dispose(): void {
    this.directionalLight.dispose()
  }
}
