import * as THREE from 'three'
import { beforeEach, describe, expect, it } from 'vitest'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { SolarScene } from '../scene/SolarScene'
import { useCorrectionsStore } from '../store/correctionsStore'

/**
 * FR-025 — a height correction has to reach the extruded geometry that casts the shadow, through
 * exactly the path the overlay uses: the corrections store, the overlay's own DTO mapping, and
 * `rebuildBuildings`. Asserted end to end because "the height field has no effect on the shadow"
 * is indistinguishable, from the screen, between a broken write and a correction that is real but
 * too small to see — and only one of those is a bug.
 */

const SITE_KEY = 'site:25.155500,55.221500'

function fakeDrawingSpace(): DrawingSpaceHandle {
  return { group: new THREE.Group(), invalidate: () => {}, onFrame: () => {}, declareDrawingRequirement: () => {} }
}

function building(id: string, latitude: number, longitude: number, heightMetres: number, isSiteBuilding = false): SiteBuildingDto {
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
    heightProvenance: 'assumed',
    name: id,
    isSiteBuilding,
  }
}

/** The overlay's own mapping (SolarAnalysisOverlay.tsx), reproduced so this test exercises the
 * shape of data the scene actually receives rather than a hand-built list. */
function applyCorrections(buildings: SiteBuildingDto[], siteKey: string): SiteBuildingDto[] {
  const corrections = useCorrectionsStore.getState().bySiteKey[siteKey]
  return buildings.map((b) =>
    corrections?.buildingHeights[b.id] !== undefined ? { ...b, heightMetres: corrections.buildingHeights[b.id] } : b,
  )
}

/**
 * The tallest extrusion in the scene's single merged building mesh, read off its bounds.
 *
 * Since T031 every footprint is merged into one geometry, so there is no per-building mesh to look
 * up any more; what a height correction changes, and what this test therefore measures, is the
 * merged mesh's vertical extent.
 */
function tallestExtrusionMetres(scene: SolarScene): number {
  const mesh = scene.buildingsGroup.children[0] as THREE.Mesh
  mesh.geometry.computeBoundingBox()
  const box = mesh.geometry.boundingBox!
  return box.max.z - box.min.z
}

/** Every distinct extrusion height present in the merged geometry, so a correction applied to one
 * building can be told apart from one applied to all of them. */
function extrusionHeights(scene: SolarScene): number[] {
  const mesh = scene.buildingsGroup.children[0] as THREE.Mesh
  const position = mesh.geometry.attributes.position
  const heights = new Set<number>()
  for (let i = 0; i < position.count; i++) {
    const z = position.getZ(i)
    if (z > 1e-6) heights.add(Number(z.toFixed(6)))
  }
  return [...heights].sort((a, b) => a - b)
}

describe('Site building height correction reaches the shadow-casting geometry (FR-025)', () => {
  beforeEach(() => {
    sceneAnchor.set({ latitude: 25.1555, longitude: 55.2215 })
    useCorrectionsStore.getState().reset(SITE_KEY)
  })

  it('changes the extruded depth of the corrected building, and only that building', () => {
    // The Badr case: every OSM footprint without a height tag defaults to the same assumed 9 m
    // (OverpassBuildingFootprintProvider.DefaultHeightMetres), and exactly one is the site building.
    const buildings = [
      building('osm_way_1', 25.1556, 55.2216, 9, true),
      building('osm_way_2', 25.1554, 55.2214, 9),
      building('osm_way_3', 25.1557, 55.2213, 9),
    ]

    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    // All three start at the same assumed height, so the merged geometry holds exactly one.
    expect(extrusionHeights(scene)).toEqual([9])

    const result = useCorrectionsStore.getState().setBuildingHeight(SITE_KEY, 'osm_way_1', 1)
    expect(result.ok).toBe(true)

    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    // The corrected building is now 1 m while its two neighbours are untouched at 9 m — which is
    // exactly what "the correction reached the geometry, and only that building" looks like once
    // the footprints share one geometry.
    expect(extrusionHeights(scene)).toEqual([1, 9])
    expect(tallestExtrusionMetres(scene)).toBeCloseTo(9, 6)

    scene.disposeAll()
  })

  it('leaves the shadow radius untouched when a taller neighbour still sets the tallest height', () => {
    // Why the correction can be real and still change nothing visible about the rig: the radius is
    // driven by the TALLEST footprint, and shortening one of many identical ones does not move it.
    const buildings = [building('osm_way_1', 25.1556, 55.2216, 9, true), building('osm_way_2', 25.1554, 55.2214, 9)]

    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    scene.aimSun(270, 30)
    const before = scene.radiusMetres

    useCorrectionsStore.getState().setBuildingHeight(SITE_KEY, 'osm_way_1', 1)
    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    scene.aimSun(270, 30)

    expect(scene.radiusMetres).toBeCloseTo(before, 6)
    scene.disposeAll()
  })

  it('does move the shadow radius when the corrected building IS the tallest', () => {
    const buildings = [building('osm_way_1', 25.1556, 55.2216, 9, true)]

    const scene = new SolarScene(fakeDrawingSpace())
    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    scene.aimSun(270, 30)
    const before = scene.radiusMetres

    useCorrectionsStore.getState().setBuildingHeight(SITE_KEY, 'osm_way_1', 120)
    scene.rebuildBuildings(applyCorrections(buildings, SITE_KEY))
    scene.aimSun(270, 30)

    expect(scene.radiusMetres).toBeGreaterThan(before)
    scene.disposeAll()
  })
})
