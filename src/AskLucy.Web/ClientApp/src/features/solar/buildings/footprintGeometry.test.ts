import * as THREE from 'three'
import { beforeEach, describe, expect, it } from 'vitest'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import { worldToLocal } from '../../../viewer/api/coordinateFrame'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { buildFootprintGeometry, buildFootprintMeshes, setShowMass } from './footprintGeometry'

const RING: SiteBuildingDto['ring'] = [
  { latitude: 25.1560, longitude: 55.2210 },
  { latitude: 25.1560, longitude: 55.2220 },
  { latitude: 25.1550, longitude: 55.2220 },
  { latitude: 25.1550, longitude: 55.2210 },
  { latitude: 25.1560, longitude: 55.2210 },
]

/** A ring offset from RING so two buildings do not occupy the same ground. */
const OTHER_RING: SiteBuildingDto['ring'] = RING.map((p) => ({ latitude: p.latitude + 0.002, longitude: p.longitude }))

function makeBuilding(overrides: Partial<SiteBuildingDto> = {}): SiteBuildingDto {
  return {
    id: 'osm_way_1',
    ring: RING,
    heightMetres: 30,
    heightProvenance: 'known',
    name: 'Test Tower',
    isSiteBuilding: false,
    ...overrides,
  }
}

/** The extrusion depth, read off the geometry's own bounds rather than `ExtrudeGeometry.parameters`
 * — after T031's merge there is one geometry for every building, and it no longer carries the
 * per-building parameters that built it. */
function heightExtentOf(geometry: THREE.BufferGeometry): number {
  geometry.computeBoundingBox()
  const box = geometry.boundingBox!
  return box.max.z - box.min.z
}

beforeEach(() => {
  sceneAnchor.set({ latitude: 25.1555, longitude: 55.2215 })
})

describe('buildFootprintGeometry (research D13, FR-041)', () => {
  it('extrudes to a depth equal to the resolved height', () => {
    const geometry = buildFootprintGeometry(makeBuilding({ heightMetres: 42 }))
    expect(geometry).not.toBeNull()
    expect((geometry!.parameters as { options: { depth: number } }).options.depth).toBe(42)
  })

  it('positions every vertex through worldToLocal against the viewer single reference point', () => {
    const geometry = buildFootprintGeometry(makeBuilding())!
    const position = geometry.attributes.position

    // The first vertex of the extrusion's front face should match worldToLocal(RING[0]).
    const expected = worldToLocal(RING[0], 0)
    let matched = false
    for (let i = 0; i < position.count; i++) {
      if (Math.abs(position.getX(i) - expected.x) < 1e-6 && Math.abs(position.getY(i) - expected.y) < 1e-6) {
        matched = true
        break
      }
    }
    expect(matched).toBe(true)
  })

  it('returns null for a degenerate ring (fewer than 3 usable points)', () => {
    expect(buildFootprintGeometry(makeBuilding({ ring: [RING[0], RING[1]] }))).toBeNull()
  })

  it('carries a colour attribute on every vertex, so the merge can keep the site building distinct', () => {
    // T031, FR-015 — the site/neighbour distinction used to be a second material. With one shared
    // material it has to survive as a per-vertex attribute, or the merge drops it silently.
    const site = buildFootprintGeometry(makeBuilding({ isSiteBuilding: true }))!
    const neighbour = buildFootprintGeometry(makeBuilding({ isSiteBuilding: false }))!

    expect(site.attributes.color.count).toBe(site.attributes.position.count)
    expect(neighbour.attributes.color.count).toBe(neighbour.attributes.position.count)

    const siteColour = [site.attributes.color.getX(0), site.attributes.color.getY(0), site.attributes.color.getZ(0)]
    const neighbourColour = [neighbour.attributes.color.getX(0), neighbour.attributes.color.getY(0), neighbour.attributes.color.getZ(0)]
    expect(siteColour).not.toEqual(neighbourColour)
  })
})

describe('buildFootprintMeshes merges every footprint into one mesh (T028, FR-015, FR-016)', () => {
  it('produces exactly one mesh and one material for N buildings', () => {
    const buildings = [
      makeBuilding({ id: 'a', ring: RING, heightMetres: 30, isSiteBuilding: true }),
      makeBuilding({ id: 'b', ring: OTHER_RING, heightMetres: 12 }),
      makeBuilding({ id: 'c', ring: RING, heightMetres: 55 }),
    ]

    const result = buildFootprintMeshes(buildings, false)

    expect(result.mesh).not.toBeNull()
    expect(Array.isArray(result.mesh!.material)).toBe(false)
    expect(result.buildingIds).toEqual(['a', 'b', 'c'])
    // One geometry holding every building's vertices, not three geometries.
    expect(result.mesh!.geometry.groups.length).toBe(0)
    expect(heightExtentOf(result.mesh!.geometry)).toBeCloseTo(55, 6)
    expect(result.tallestHeightMetres).toBe(55)

    const singleVertexCount = buildFootprintGeometry(buildings[0])!.attributes.position.count
    expect(result.mesh!.geometry.attributes.position.count).toBeGreaterThan(singleVertexCount)

    result.mesh!.geometry.dispose()
  })

  it('reveals all of them together when the mass toggle is flipped', () => {
    // FR-016 — with one shared material the toggle can no longer be per-building, so the test that
    // matters is that no building is left hidden behind.
    const result = buildFootprintMeshes(
      [makeBuilding({ id: 'a' }), makeBuilding({ id: 'b', ring: OTHER_RING })],
      false,
    )
    const material = result.mesh!.material as THREE.MeshStandardMaterial
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(false)

    setShowMass(result.mesh!, true)
    expect(material.colorWrite).toBe(true)
    expect(material.depthWrite).toBe(true)
    // Both buildings share that one material, so both are revealed by the single flip.
    expect(result.buildingIds).toEqual(['a', 'b'])

    setShowMass(result.mesh!, false)
    expect(material.colorWrite).toBe(false)
    result.mesh!.geometry.dispose()
  })

  it('excludes a degenerate ring and counts it, without failing the merge', () => {
    // T032, FR-028 — the merge must survive an unusable footprint, and the count must reach the
    // caller so the exclusion can be stated rather than silently dropped.
    const result = buildFootprintMeshes(
      [
        makeBuilding({ id: 'good', heightMetres: 20 }),
        makeBuilding({ id: 'degenerate', ring: [RING[0], RING[1]] }),
      ],
      false,
    )

    expect(result.excludedCount).toBe(1)
    expect(result.buildingIds).toEqual(['good'])
    expect(result.mesh).not.toBeNull()
    expect(heightExtentOf(result.mesh!.geometry)).toBeCloseTo(20, 6)
    // The excluded ring must not have contributed to the bounds it would have widened.
    expect(result.tallestHeightMetres).toBe(20)

    result.mesh!.geometry.dispose()
  })
})

describe('buildFootprintMeshes with nothing usable (T029, FR-017)', () => {
  it('produces no mesh at all rather than a mesh wrapping empty geometry', () => {
    const result = buildFootprintMeshes([], false)

    expect(result.mesh).toBeNull()
    expect(result.buildingIds).toEqual([])
    expect(result.extentMetres).toBe(0)
    expect(result.tallestHeightMetres).toBe(0)
    expect(result.excludedCount).toBe(0)
  })

  it('produces no mesh when every supplied footprint is degenerate', () => {
    const result = buildFootprintMeshes(
      [makeBuilding({ id: 'a', ring: [RING[0], RING[1]] }), makeBuilding({ id: 'b', ring: [RING[0]] })],
      false,
    )

    expect(result.mesh).toBeNull()
    expect(result.excludedCount).toBe(2)
  })

  it('keeps castShadow / colorWrite:false / depthWrite:false semantics unchanged', () => {
    // research D13 — buildings cast without being drawn. The merge changed how many meshes carry
    // this, never whether it holds.
    const result = buildFootprintMeshes([makeBuilding()], false)
    const material = result.mesh!.material as THREE.MeshStandardMaterial

    expect(result.mesh!.castShadow).toBe(true)
    expect(result.mesh!.receiveShadow).toBe(true)
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(false)
    expect(material.vertexColors).toBe(true)

    result.mesh!.geometry.dispose()
  })
})
