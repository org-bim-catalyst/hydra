import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { SolarScene } from './SolarScene'

const DUBAI = { latitude: 25.2, longitude: 55.3 }
const HOUR_LINE_COLOR = '#524E46'

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
  it('parents the dome groups to the overlay group and nothing else there', () => {
    const drawingSpace = fakeDrawingSpace()
    const scene = populatedScene(drawingSpace)

    expect(drawingSpace.overlayGroup.children).toEqual([scene.sunPathFixedGroup, scene.sunPathDatedGroup])
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
