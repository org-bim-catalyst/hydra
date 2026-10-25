import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { OVERLAY_FLOOR_LAYER } from '../../../viewer/scene/overlayLayers'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { SolarScene } from './SolarScene'

const DUBAI = { latitude: 25.2, longitude: 55.3 }
const HOUR_LINE_COLOR = '#524E46'

/** A square footprint centred on the reference point, so it straddles the dome. */
function siteBuilding(halfWidthMetres: number, heightMetres: number): SiteBuildingDto {
  const dLat = halfWidthMetres / 111_320
  const dLon = halfWidthMetres / (111_320 * Math.cos((DUBAI.latitude * Math.PI) / 180))
  return {
    id: 'site',
    ring: [
      { latitude: DUBAI.latitude - dLat, longitude: DUBAI.longitude - dLon },
      { latitude: DUBAI.latitude - dLat, longitude: DUBAI.longitude + dLon },
      { latitude: DUBAI.latitude + dLat, longitude: DUBAI.longitude + dLon },
      { latitude: DUBAI.latitude + dLat, longitude: DUBAI.longitude - dLon },
    ],
    heightMetres,
    heightProvenance: 'known',
    name: 'site',
    isSiteBuilding: true,
  }
}

function fakeDrawingSpace(): DrawingSpaceHandle {
  return {
    group: new THREE.Group(),
    overlayGroup: new THREE.Group(),
    invalidate: () => {},
    onFrame: () => {},
    declareDrawingRequirement: () => {},
  }
}

/** A scene with every group populated: dome furniture and arcs, massing, the shadow rig. */
function populatedScene(drawingSpace: DrawingSpaceHandle): SolarScene {
  const scene = new SolarScene(drawingSpace)
  scene.updateSunPath(
    '2026-09-22',
    DUBAI.latitude,
    DUBAI.longitude,
    new Date(Date.UTC(2026, 8, 22, 10)),
    180,
    45,
    'Asia/Dubai',
    HOUR_LINE_COLOR,
  )
  scene.rebuildBuildings([])
  return scene
}

describe('the dome lives on the overlay surface, the analysis in the map (specs/087 FR-002, FR-003)', () => {
  it('parents the dome groups to the overlay group and the massing to the map pass', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)

    expect(drawingSpace.overlayGroup.children).toEqual([
      scene.domeOccluderGroup,
      scene.sunPathFixedGroup,
      scene.sunPathDatedGroup,
    ])
    expect(drawingSpace.group.children).toEqual([scene.buildingsGroup, scene.groundGroup, scene.lightGroup])
    expect(drawingSpace.overlayGroup.children).not.toContain(scene.buildingsGroup)
    scene.disposeAll()
  })

  it('keeps the massing, shadow ground and sun light out of the overlay entirely', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)

    for (const group of [scene.buildingsGroup, scene.groundGroup, scene.lightGroup]) {
      group.traverse((child) => expect(drawingSpace.overlayGroup.children).not.toContain(child))
    }
    scene.disposeAll()
  })

  it('moves both surfaces with the ground offset — the dome must follow the massing it describes', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)

    scene.setGroundOffset(7.5)

    expect(drawingSpace.group.position.z).toBe(7.5)
    expect(drawingSpace.overlayGroup.position.z).toBe(7.5)
    scene.disposeAll()
  })

  it('draws the dial — and only the dial — in the floor pass, so buildings can hide it', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)
    const dial = scene.sunPathFixedGroup.children[0]
    const onLayer = (object: THREE.Object3D, channel: number) => (object.layers.mask & (1 << channel)) !== 0

    expect(onLayer(dial, OVERLAY_FLOOR_LAYER)).toBe(true)
    expect(onLayer(dial, 0)).toBe(false)
    for (const child of scene.sunPathFixedGroup.children.slice(1)) {
      expect(onLayer(child, 0)).toBe(true)
    }
    scene.disposeAll()
  })

  it('mirrors the footprints as a depth-only occluder on the floor layer, sharing their geometry', () => {
    sceneAnchor.set(DUBAI)
    const drawingSpace = fakeDrawingSpace()
    const scene = new SolarScene(drawingSpace)
    scene.rebuildBuildings([siteBuilding(40, 20)])

    const occluder = scene.domeOccluderGroup.children[0] as THREE.Mesh
    const massing = scene.buildingsGroup.children[0] as THREE.Mesh
    const material = occluder.material as THREE.MeshBasicMaterial

    expect(occluder.geometry).toBe(massing.geometry)
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(true)
    expect(material.depthTest).toBe(true)
    expect((occluder.layers.mask & (1 << OVERLAY_FLOOR_LAYER)) !== 0).toBe(true)
    scene.disposeAll()
  })

  it('keeps occluding when the massing itself is hidden', () => {
    sceneAnchor.set(DUBAI)
    const drawingSpace = fakeDrawingSpace()
    const scene = new SolarScene(drawingSpace)
    scene.rebuildBuildings([siteBuilding(40, 20)])
    const occluder = scene.domeOccluderGroup.children[0] as THREE.Mesh

    scene.setShowBuildingMass(false)
    const material = occluder.material as THREE.MeshBasicMaterial

    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(true)
    scene.disposeAll()
  })

  it('hides and shows both surfaces together (FR-014)', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)

    scene.setVisible(false)
    expect(drawingSpace.group.visible).toBe(false)
    expect(drawingSpace.overlayGroup.visible).toBe(false)

    scene.setVisible(true)
    expect(drawingSpace.group.visible).toBe(true)
    expect(drawingSpace.overlayGroup.visible).toBe(true)
    scene.disposeAll()
  })
})
