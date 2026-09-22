import * as THREE from 'three'
import { beforeEach, describe, expect, it } from 'vitest'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { ShadowGround } from './shadowGround'
import { SolarScene } from './SolarScene'
import { NO_BUILDINGS_FALLBACK_EXTENT_METRES, SunLight } from './sunLight'

describe('ShadowGround / SunLight frustum sizing (FR-009, research D16)', () => {
  it("the ground plane's half-extent is strictly less than the shadow frustum's half-extent, for any radius", () => {
    // The reference implementation's own documented failure: a ground plane reaching outside the
    // shadow camera's frustum produces clamped shadow-map lookups that read as "in shadow" — a
    // large fake grey blob unrelated to real geometry. This must never regress.
    for (const radius of [50, 100, 200, 500, 1000, 5000]) {
      const ground = new ShadowGround(radius)
      expect(ground.groundHalfExtentFor(radius)).toBeLessThan(ShadowGround.frustumHalfExtentFor(radius))
    }
  })

  it('resizes by scaling the plane rather than rebuilding its geometry', () => {
    // Since T023 the radius tracks the sun's elevation, so this resize happens on a time-of-day
    // tick. Rebuilding PlaneGeometry there is precisely the per-tick cost US3 exists to remove.
    const ground = new ShadowGround(200)
    const geometryBefore = ground.mesh.geometry
    expect(ground.mesh.scale.x).toBeCloseTo(400, 5)

    ground.setRadius(500)
    expect(ground.mesh.geometry).toBe(geometryBefore)
    expect(ground.mesh.scale.x).toBeCloseTo(1000, 5)
    expect(ground.radiusMetres).toBe(500)
  })

  it('the actual shadow camera frustum, constructed via SunLight, exceeds the ground plane half-extent', () => {
    const radius = 200
    const light = new SunLight(radius)
    const ground = new ShadowGround(radius)
    const camera = light.directionalLight.shadow.camera as THREE.OrthographicCamera

    expect(camera.right).toBeGreaterThan(ground.groundHalfExtentFor(radius))
  })

  it('is receiveShadow, and hidden when nothing is casting (FR-012, FR-017)', () => {
    const ground = new ShadowGround(200)
    expect(ground.mesh.receiveShadow).toBe(true)
    ground.setVisible(false)
    expect(ground.mesh.visible).toBe(false)
    ground.setVisible(true)
    expect(ground.mesh.visible).toBe(true)
  })
})

/*
 * T018, FR-009, FR-010, SC-007 — the same invariant, now carried through the *content-derived*
 * radius rather than a fixed multiple of the radius the buildings were queried with. Each shape of
 * site produces a different radius, and since T023 that radius also moves with the sun; none of
 * those combinations may put the ground plane outside the frustum.
 *
 * These read the real light and the real ground mesh out of the scene's own groups, not a helper
 * that recomputes the same arithmetic — what matters is what was actually built.
 */
const DUBAI = { latitude: 25.1555, longitude: 55.2215 }

function fakeDrawingSpace(): DrawingSpaceHandle {
  return { group: new THREE.Group(), invalidate: () => {}, onFrame: () => {}, declareDrawingRequirement: () => {} }
}

function building(id: string, latitude: number, longitude: number, heightMetres: number): SiteBuildingDto {
  const size = 0.0002
  return {
    id,
    ring: [
      { latitude, longitude },
      { latitude, longitude: longitude + size },
      { latitude: latitude - size, longitude: longitude + size },
      { latitude: latitude - size, longitude },
    ],
    heightMetres,
    heightProvenance: 'known',
    name: id,
    isSiteBuilding: false,
  }
}

/** The site shapes FR-009/FR-010 have to hold across — "regardless of building layout". */
const SITE_SHAPES: Record<string, SiteBuildingDto[]> = {
  'a tight cluster of low-rise': [
    building('a', 25.1556, 55.2216, 9),
    building('b', 25.1554, 55.2214, 12),
    building('c', 25.1557, 55.2213, 8),
  ],
  'a lone tall building': [building('tower', 25.1555, 55.2215, 180)],
  'an off-centre cluster': [building('d', 25.1585, 55.2245, 40), building('e', 25.1587, 55.2248, 25)],
  'no buildings at all': [],
}

function shadowCameraOf(scene: SolarScene): THREE.OrthographicCamera {
  const light = scene.lightGroup.children.find((child) => child instanceof THREE.DirectionalLight) as THREE.DirectionalLight
  return light.shadow.camera as THREE.OrthographicCamera
}

/** The ground mesh is a unit plane scaled to its full width, so half of that scale is the furthest
 * ground point from the reference point — the quantity the frustum has to contain. */
function groundHalfExtentOf(scene: SolarScene): number {
  const mesh = scene.groundGroup.children.find((child) => child instanceof THREE.Mesh) as THREE.Mesh
  return mesh.scale.x / 2
}

describe('Shadow rig sized from content (T018, FR-009, FR-010, SC-005, SC-007)', () => {
  beforeEach(() => {
    sceneAnchor.set(DUBAI)
  })

  it.each(Object.keys(SITE_SHAPES))(
    'keeps the ground plane strictly inside the frustum at every sun elevation — %s',
    (shape) => {
      const scene = new SolarScene(fakeDrawingSpace())
      scene.rebuildBuildings(SITE_SHAPES[shape])

      // "No sun position", so sweep the whole day including the low-sun window and the night,
      // not one convenient midday value.
      for (let altitude = -10; altitude <= 90; altitude += 0.5) {
        scene.aimSun(135, altitude)
        const camera = shadowCameraOf(scene)

        expect(Number.isFinite(camera.right)).toBe(true)
        expect(groundHalfExtentOf(scene)).toBeLessThan(camera.right)
      }

      scene.disposeAll()
    },
  )

  it('falls back to a stated extent when the site has no buildings (FR-013)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings([])
    scene.aimSun(135, 45)

    // Nothing casts, so the radius is the fallback extent exactly — there is no shadow to contain.
    expect(scene.radiusMetres).toBeCloseTo(NO_BUILDINGS_FALLBACK_EXTENT_METRES, 5)
    scene.disposeAll()
  })

  it('fits a tight cluster far tighter than a sprawling one, which is the resolution gain (SC-005)', () => {
    const tight = new SolarScene(fakeDrawingSpace())
    tight.rebuildBuildings(SITE_SHAPES['a tight cluster of low-rise'])
    tight.aimSun(135, 45)

    const sprawling = new SolarScene(fakeDrawingSpace())
    sprawling.rebuildBuildings(SITE_SHAPES['an off-centre cluster'])
    sprawling.aimSun(135, 45)

    expect(tight.radiusMetres).toBeLessThan(sprawling.radiusMetres)
    // The tight cluster also fits well inside the 200 m the previous release used regardless of
    // content; that reclaimed slack is what buys the extra shadow-map resolution.
    expect(tight.radiusMetres).toBeLessThan(NO_BUILDINGS_FALLBACK_EXTENT_METRES)

    tight.disposeAll()
    sprawling.disposeAll()
  })

  it('resizes the rig when the buildings change, without waiting for the next time tick (FR-010)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings(SITE_SHAPES['a tight cluster of low-rise'])
    scene.aimSun(135, 30)
    const tightRadius = scene.radiusMetres

    // A correction raising a building's height lengthens its shadow; the rig has to follow, or the
    // new shadow is truncated at a boundary the user cannot see.
    scene.rebuildBuildings([building('a', 25.1556, 55.2216, 120)])
    expect(scene.radiusMetres).toBeGreaterThan(tightRadius)
    expect(groundHalfExtentOf(scene)).toBeLessThan(shadowCameraOf(scene).right)

    scene.disposeAll()
  })

  it('hides the ground plane in the low-sun window, where no shadows are drawn (FR-012)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings(SITE_SHAPES['a tight cluster of low-rise'])

    scene.aimSun(90, 0.5)
    const mesh = scene.groundGroup.children.find((child) => child instanceof THREE.Mesh) as THREE.Mesh
    expect(mesh.visible).toBe(false)

    scene.aimSun(90, 20)
    expect(mesh.visible).toBe(true)

    scene.disposeAll()
  })
})
