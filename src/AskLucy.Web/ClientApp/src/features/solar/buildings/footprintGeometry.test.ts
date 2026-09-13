import * as THREE from 'three'
import { beforeEach, describe, expect, it } from 'vitest'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import { worldToLocal } from '../../../viewer/api/coordinateFrame'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { buildFootprintMesh, setShowMass } from './footprintGeometry'

const RING: SiteBuildingDto['ring'] = [
  { latitude: 25.1560, longitude: 55.2210 },
  { latitude: 25.1560, longitude: 55.2220 },
  { latitude: 25.1550, longitude: 55.2220 },
  { latitude: 25.1550, longitude: 55.2210 },
  { latitude: 25.1560, longitude: 55.2210 },
]

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

beforeEach(() => {
  sceneAnchor.set({ latitude: 25.1555, longitude: 55.2215 })
})

describe('buildFootprintMesh (research D13, FR-041)', () => {
  it('extrudes to a depth equal to the resolved height', () => {
    const mesh = buildFootprintMesh(makeBuilding({ heightMetres: 42 }), false)
    expect(mesh).not.toBeNull()
    const geometry = mesh!.geometry as THREE.ExtrudeGeometry
    expect((geometry.parameters as { options: { depth: number } }).options.depth).toBe(42)
  })

  it('has colorWrite=false and castShadow=true by default (buildings cast but are not drawn)', () => {
    const mesh = buildFootprintMesh(makeBuilding(), false)!
    const material = mesh.material as THREE.MeshStandardMaterial
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(false)
    expect(mesh.castShadow).toBe(true)
  })

  it('the showMass toggle flips colorWrite/depthWrite back on', () => {
    const mesh = buildFootprintMesh(makeBuilding(), true)!
    const material = mesh.material as THREE.MeshStandardMaterial
    expect(material.colorWrite).toBe(true)
    expect(material.depthWrite).toBe(true)

    setShowMass(mesh, false)
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(false)
  })

  it('positions every vertex through worldToLocal against the viewer single reference point', () => {
    const mesh = buildFootprintMesh(makeBuilding(), false)!
    const geometry = mesh.geometry as THREE.ExtrudeGeometry
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
    const mesh = buildFootprintMesh(makeBuilding({ ring: [RING[0], RING[1]] }), false)
    expect(mesh).toBeNull()
  })
})
