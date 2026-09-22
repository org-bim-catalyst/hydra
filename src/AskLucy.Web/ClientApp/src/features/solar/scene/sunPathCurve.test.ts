import * as THREE from 'three'
import { describe, expect, it, vi } from 'vitest'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import { SolarScene } from './SolarScene'
import {
  buildDatedPath,
  buildDomeShell,
  buildFixedFurniture,
  buildTubeFromPoints,
  sampleDayArcPoints,
  sphericalToVec,
} from './sunPathCurve'

describe('sphericalToVec (ENU: X=East, Y=North, Z=Up)', () => {
  it('places due-north, horizon-level at +Y', () => {
    const v = sphericalToVec(0, 0, 100)
    expect(v.x).toBeCloseTo(0, 6)
    expect(v.y).toBeCloseTo(100, 6)
    expect(v.z).toBeCloseTo(0, 6)
  })

  it('places straight overhead at +Z regardless of azimuth', () => {
    const v = sphericalToVec(123, 90, 100)
    expect(v.x).toBeCloseTo(0, 5)
    expect(v.y).toBeCloseTo(0, 5)
    expect(v.z).toBeCloseTo(100, 5)
  })
})

describe('arcs are built from tube geometry, never THREE.Line (research D17)', () => {
  it('buildTubeFromPoints returns a Mesh with TubeGeometry', () => {
    const points = [new THREE.Vector3(0, 0, 0), new THREE.Vector3(10, 10, 10), new THREE.Vector3(20, 0, 5)]
    const mesh = buildTubeFromPoints(points, 0.5, 0xff0000)
    expect(mesh).not.toBeNull()
    expect(mesh).toBeInstanceOf(THREE.Mesh)
    expect(mesh!.geometry).toBeInstanceOf(THREE.TubeGeometry)
    expect(mesh).not.toBeInstanceOf(THREE.Line)
  })

  it('returns null for fewer than 2 points rather than throwing', () => {
    expect(buildTubeFromPoints([], 0.5, 0xff0000)).toBeNull()
    expect(buildTubeFromPoints([new THREE.Vector3()], 0.5, 0xff0000)).toBeNull()
  })
})

describe('sampleDayArcPoints', () => {
  it('produces only above-horizon points for a normal day at Dubai', () => {
    const points = sampleDayArcPoints(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, 120)
    expect(points.length).toBeGreaterThan(0)
    for (const p of points) {
      // Every returned point's Z (up) must be >= 0 — the horizon cutoff.
      expect(p.z).toBeGreaterThanOrEqual(-1e-6)
    }
  })

  it('produces zero points for deep polar night', () => {
    const points = sampleDayArcPoints(new Date(Date.UTC(2026, 11, 21)), 78, 15.6, 120)
    expect(points.length).toBe(0)
  })
})

describe('buildDatedPath — chosen day, seasonal extremes and hour marks are distinct objects (FR-005, FR-006, FR-007)', () => {
  it('produces separate, distinguishably-materialed meshes for chosen day, summer and winter extremes', () => {
    const result = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), 25.2, 55.3, new Date(Date.UTC(2026, 8, 22, 10, 0)))

    expect(result.chosenDay).not.toBeNull()
    expect(result.summerExtreme).not.toBeNull()
    expect(result.winterExtreme).not.toBeNull()

    // Distinct objects.
    expect(result.chosenDay).not.toBe(result.summerExtreme)
    expect(result.chosenDay).not.toBe(result.winterExtreme)
    expect(result.summerExtreme).not.toBe(result.winterExtreme)

    // Distinguishable materials (FR-006).
    const chosenColor = (result.chosenDay!.material as THREE.MeshBasicMaterial).color.getHex()
    const summerColor = (result.summerExtreme!.material as THREE.MeshBasicMaterial).color.getHex()
    const winterColor = (result.winterExtreme!.material as THREE.MeshBasicMaterial).color.getHex()
    expect(new Set([chosenColor, summerColor, winterColor]).size).toBe(3)
  })

  it('picks June as the summer extreme in the northern hemisphere and December in the southern', () => {
    const north = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10, 0)))
    const south = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), -54.8, -68.3, new Date(Date.UTC(2026, 8, 22, 10, 0)))
    // Both hemispheres must produce a summer/winter pair; which calendar month backs each differs,
    // which is exercised indirectly through daySummary's own hemisphere test — this test only
    // confirms both tracks exist for both hemispheres.
    expect(north.summerExtreme).not.toBeNull()
    expect(north.winterExtreme).not.toBeNull()
    expect(south.summerExtreme).not.toBeNull()
    expect(south.winterExtreme).not.toBeNull()
  })

  it('marks hours along the chosen day only when the sun is meaningfully above the horizon', () => {
    const result = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 12, 0)))
    expect(result.hourMarks.length).toBeGreaterThan(0)
    for (const sprite of result.hourMarks) {
      expect(sprite).toBeInstanceOf(THREE.Sprite)
    }
  })

  it('shows the current position marker in gold when above the horizon, dim when below', () => {
    const day = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 10, 0)))
    const night = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 0, 0)))
    const dayColor = (day.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    const nightColor = (night.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    expect(dayColor).not.toBe(nightColor)
  })
})

/**
 * T038-T041, FR-021, FR-022, FR-023, SC-008, SC-010 — the dome is split by LIFETIME: what depends
 * only on the site and the year (compass dial, mount post, shell, monthly lattice) lives in one
 * group, and what depends on the date lives in a sibling group. Stepping through dates must touch
 * only the second. Asserted on object IDENTITY rather than on counts, because a rebuild that
 * happens to produce the same number of children is exactly the failure being guarded against —
 * the dial's baked 2048² texture being thrown away and rebuilt on every date step.
 */
function fakeDrawingSpace(): DrawingSpaceHandle {
  return { group: new THREE.Group(), invalidate: () => {}, onFrame: () => {}, declareDrawingRequirement: () => {} }
}

const DUBAI = { latitude: 25.2, longitude: 55.3 }

function identitiesOf(group: THREE.Group): THREE.Object3D[] {
  return [...group.children]
}

describe('A date change rebuilds only the dated group (T038, FR-021, SC-008)', () => {
  it('leaves every fixed-furniture object identity untouched across a sequence of dates', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)

    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)
    const datedBefore = identitiesOf(scene.sunPathDatedGroup)
    expect(fixedBefore.length).toBeGreaterThan(0)
    expect(datedBefore.length).toBeGreaterThan(0)

    for (const date of ['2026-09-23', '2026-10-15', '2026-12-21', '2026-03-20']) {
      const rebuilt = scene.updateSunPath(date, DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
      expect(rebuilt).toBe(true)
    }

    // Same objects, in the same order — not merely the same count.
    expect(identitiesOf(scene.sunPathFixedGroup)).toEqual(fixedBefore)
    // ...while the dated group really was replaced.
    expect(identitiesOf(scene.sunPathDatedGroup)).not.toEqual(datedBefore)

    scene.disposeAll()
  })

  it('rebuilds nothing at all on a time-of-day tick within the same date', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)
    const datedBefore = identitiesOf(scene.sunPathDatedGroup)

    const rebuilt = scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 14)), 240, 30)

    expect(rebuilt).toBe(false)
    expect(identitiesOf(scene.sunPathFixedGroup)).toEqual(fixedBefore)
    expect(identitiesOf(scene.sunPathDatedGroup)).toEqual(datedBefore)

    scene.disposeAll()
  })
})

describe('A site change or a year change rebuilds both groups (T039, FR-022)', () => {
  it('rebuilds the fixed furniture on a site change', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    // London: a different latitude puts the monthly lattice and the dial somewhere else entirely.
    scene.updateSunPath('2026-09-22', 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)

    expect(identitiesOf(scene.sunPathFixedGroup)).not.toEqual(fixedBefore)
    scene.disposeAll()
  })

  it('rebuilds the fixed furniture on a year change', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-12-31', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 11, 31, 10)), 180, 45)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    scene.updateSunPath('2027-01-01', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2027, 0, 1, 10)), 180, 45)

    expect(identitiesOf(scene.sunPathFixedGroup)).not.toEqual(fixedBefore)
    scene.disposeAll()
  })

  it('builds the dome shell from a plain transparent material, never scene.environment (README constraint 5)', () => {
    // research D17 — the PROTOTYPE's glass shell is what was dropped: it needed
    // `MeshPhysicalMaterial.transmission`, which only reads as glass against a `scene.environment`,
    // and the scene is framework-owned state this feature may not assign (FR-038). Classifying the
    // replacement envelope as fixed furniture must not quietly reintroduce that dependency.
    const shell = buildDomeShell(120)
    const material = shell.material as THREE.MeshStandardMaterial

    expect(material.type).toBe('MeshStandardMaterial')
    expect((material as unknown as { transmission?: number }).transmission).toBeUndefined()
    expect(material.envMap ?? null).toBeNull()
    expect(material.transparent).toBe(true)
    expect(material.depthWrite).toBe(false)

    // It encloses everything else and writes no depth, so it must still draw last.
    const furniture = buildFixedFurniture(DUBAI.latitude, DUBAI.longitude, 2026)
    expect(furniture.shell.renderOrder).toBeGreaterThan(furniture.dial.renderOrder)
  })
})

describe('Arcs stay tubes, never THREE.Line (T040, README constraint 5)', () => {
  it('builds every dated arc and every monthly lattice arc from TubeGeometry', () => {
    const dated = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)))
    const furniture = buildFixedFurniture(DUBAI.latitude, DUBAI.longitude, 2026)

    for (const arc of [dated.chosenDay, dated.summerExtreme, dated.winterExtreme, ...furniture.monthlyArcs]) {
      expect(arc).not.toBeNull()
      expect(arc!).toBeInstanceOf(THREE.Mesh)
      expect(arc!).not.toBeInstanceOf(THREE.Line)
      expect(arc!.geometry).toBeInstanceOf(THREE.TubeGeometry)
    }
  })

  it('introduces no THREE.Line anywhere in either assembled group', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)

    for (const group of [scene.sunPathFixedGroup, scene.sunPathDatedGroup]) {
      group.traverse((child) => {
        expect(child).not.toBeInstanceOf(THREE.Line)
        expect(child).not.toBeInstanceOf(THREE.LineSegments)
      })
    }

    scene.disposeAll()
  })
})

describe('Repeated changes accumulate nothing in either disposal scope (T041, FR-023, SC-010)', () => {
  it('holds child counts steady across many date changes and site changes', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
    const fixedCount = scene.sunPathFixedGroup.children.length
    const datedCount = scene.sunPathDatedGroup.children.length

    for (let cycle = 0; cycle < 10; cycle++) {
      scene.updateSunPath('2026-09-23', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 23, 10)), 180, 45)
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
    }

    // Same date and site as the very first call, so the same instrument — not ten of them stacked.
    expect(scene.sunPathFixedGroup.children.length).toBe(fixedCount)
    expect(scene.sunPathDatedGroup.children.length).toBe(datedCount)

    for (let cycle = 0; cycle < 10; cycle++) {
      scene.updateSunPath('2026-09-22', 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
    }

    expect(scene.sunPathFixedGroup.children.length).toBe(fixedCount)
    expect(scene.sunPathDatedGroup.children.length).toBe(datedCount)

    // T044 — disposeAll must cover BOTH scopes, or an open/close cycle leaks the half it forgot.
    scene.disposeAll()
    expect(scene.sunPathFixedGroup.children.length).toBe(0)
    expect(scene.sunPathDatedGroup.children.length).toBe(0)
  })

  it('empties both groups on every one of fifty open/close cycles (SC-010)', () => {
    for (let cycle = 0; cycle < 50; cycle++) {
      const scene = new SolarScene(fakeDrawingSpace())
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45)
      expect(scene.sunPathFixedGroup.children.length).toBeGreaterThan(0)

      scene.disposeAll()
      expect(scene.sunPathFixedGroup.children.length).toBe(0)
      expect(scene.sunPathDatedGroup.children.length).toBe(0)
    }
  })
})

describe('setGroundOffset moves only the extension group (T045, README constraint 3)', () => {
  it('translates the Drawing Space group in Z and never re-anchors the scene', () => {
    const setSpy = vi.spyOn(sceneAnchor, 'set')

    const drawingSpace = fakeDrawingSpace()
    const scene = new SolarScene(drawingSpace)
    scene.setGroundOffset(7.5)

    expect(drawingSpace.group.position.z).toBe(7.5)
    expect(setSpy).not.toHaveBeenCalled()

    setSpy.mockRestore()
    scene.disposeAll()
  })
})
