import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { ShadowGround } from './shadowGround'
import { SunLight } from './sunLight'

describe('ShadowGround / SunLight frustum sizing (FR-018, research D16)', () => {
  it("the ground plane's half-extent is strictly less than the shadow frustum's half-extent, for any radius", () => {
    // The reference implementation's own documented failure: a ground plane reaching outside the
    // shadow camera's frustum produces clamped shadow-map lookups that read as "in shadow" — a
    // large fake grey blob unrelated to real geometry. This must never regress.
    for (const radius of [50, 100, 200, 500, 1000]) {
      const frustumHalfExtent = ShadowGround.frustumHalfExtentFor(radius)
      const ground = new ShadowGround(radius)
      const groundHalfExtent = ground.groundHalfExtentFor(radius)

      expect(groundHalfExtent).toBeLessThan(frustumHalfExtent)
    }
  })

  it('sizes the actual PlaneGeometry to radius * 2.4', () => {
    const ground = new ShadowGround(200)
    const geometry = ground.mesh.geometry as THREE.PlaneGeometry
    expect(geometry.parameters.width).toBeCloseTo(480, 5)
    expect(geometry.parameters.height).toBeCloseTo(480, 5)
  })

  it('the actual shadow camera frustum, constructed via SunLight, exceeds the ground plane half-extent', () => {
    const radius = 200
    const light = new SunLight(radius)
    const ground = new ShadowGround(radius)
    const camera = light.directionalLight.shadow.camera as THREE.OrthographicCamera

    expect(camera.right).toBeGreaterThan(ground.groundHalfExtentFor(radius))
  })

  it('is receiveShadow and hidden below the horizon (FR-016, FR-017)', () => {
    const ground = new ShadowGround(200)
    expect(ground.mesh.receiveShadow).toBe(true)
    ground.setVisible(false)
    expect(ground.mesh.visible).toBe(false)
    ground.setVisible(true)
    expect(ground.mesh.visible).toBe(true)
  })
})

/** Found in review of specs/052: `SolarScene.ensureShadowRig` reconfigured the light's frustum on
 * a radius change but kept the original ground plane, whose geometry is fixed at construction. A
 * radius DECREASE therefore left a plane larger than the new, tighter frustum — research D16's
 * clamped-lookup grey blob, reached from the opposite direction. */
describe('SolarScene.ensureShadowRig — ground plane tracks a changed radius (FR-018, research D16)', () => {
  it('keeps the ground plane strictly inside the frustum after the radius shrinks', async () => {
    const { SolarScene } = await import('./SolarScene')
    const group = new THREE.Group()
    const scene = new SolarScene({
      group,
      invalidate: () => {},
      onFrame: () => {},
      declareDrawingRequirement: () => {},
    })

    scene.ensureShadowRig(1000, 50)
    scene.ensureShadowRig(200, 50)

    const plane = group.getObjectByProperty('type', 'Mesh') as THREE.Mesh
    const geometry = plane.geometry as THREE.PlaneGeometry
    const groundHalfExtent = geometry.parameters.width / 2
    const frustumHalfExtent = ShadowGround.frustumHalfExtentFor(200)

    expect(groundHalfExtent).toBeLessThan(frustumHalfExtent)
    scene.disposeAll()
  })
})
