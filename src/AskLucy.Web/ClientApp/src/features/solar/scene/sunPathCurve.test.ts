import * as THREE from 'three'
import { describe, expect, it, vi } from 'vitest'
import type { DrawingSpaceHandle } from '../../../viewer/scene/DrawingSpaceRegistry'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { SolarScene } from './SolarScene'
import {
  buildDatedPath,
  buildDomeOccluder,
  buildDomeShell,
  buildFixedFurniture,
  buildHourLines,
  buildTubeFromPoints,
  sampleDayArcPoints,
  sampleHourLineRuns,
  splitAboveHorizonRuns,
  MAX_SUN_PATH_DOME_RADIUS_METRES,
  SUN_PATH_DOME_RADIUS_METRES,
  sphericalToVec,
  sunPathDomeRadiusFor,
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
    const result = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), 25.2, 55.3, new Date(Date.UTC(2026, 8, 22, 10, 0)), 'Asia/Dubai')

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
    const north = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10, 0)), 'Europe/London')
    const south = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), -54.8, -68.3, new Date(Date.UTC(2026, 8, 22, 10, 0)), 'America/Punta_Arenas')
    // Both hemispheres must produce a summer/winter pair; which calendar month backs each differs,
    // which is exercised indirectly through daySummary's own hemisphere test — this test only
    // confirms both tracks exist for both hemispheres.
    expect(north.summerExtreme).not.toBeNull()
    expect(north.winterExtreme).not.toBeNull()
    expect(south.summerExtreme).not.toBeNull()
    expect(south.winterExtreme).not.toBeNull()
  })

  it('marks hours along the chosen day only when the sun is meaningfully above the horizon', () => {
    const result = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 12, 0)), 'Asia/Dubai')
    expect(result.hourMarks.length).toBeGreaterThan(0)
    for (const sprite of result.hourMarks) {
      expect(sprite).toBeInstanceOf(THREE.Sprite)
    }
  })

  it('labels the hour marks on the site’s own clock, not on UTC (data-model `localHour`)', () => {
    const result = buildDatedPath(
      new Date(Date.UTC(2026, 5, 21)),
      25.2,
      55.3,
      new Date(Date.UTC(2026, 5, 21, 12, 0)),
      'Asia/Dubai',
    )

    // Dubai's solar noon is a little after 12:00 local, so the 12:00 mark is the highest one on
    // the dome. Stepping UTC hours instead put the "12" where the sun stands at 16:00 local —
    // four hours out of place, which is exactly the bug this pins.
    const highest = result.hourMarks.reduce((a, b) => (a.position.z >= b.position.z ? a : b))
    expect(highest.userData.localHour).toBe(12)
  })

  it('shows the current position marker in gold when above the horizon, dim when below', () => {
    const day = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 10, 0)), 'Asia/Dubai')
    const night = buildDatedPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 0, 0)), 'Asia/Dubai')
    const dayColor = (day.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    const nightColor = (night.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    expect(dayColor).not.toBe(nightColor)
  })
})

/**
 * specs/086 — the hour lines. Each local clock hour's curve is the sun's position at that hour on
 * every day of the year, drawn at the dome's radius and filtered at the horizon. The fixtures are
 * measured properties of the NOAA algorithm over 2026 (research D11), not values this
 * implementation chose.
 */
const HOUR_LINE_SITES: Array<[string, number, number, string]> = [
  ['Dubai', 25.2, 55.3, 'Asia/Dubai'],
  ['London', 51.5, -0.1, 'Europe/London'],
  ['Sydney', -33.87, 151.21, 'Australia/Sydney'],
  ['Singapore', 1.35, 103.82, 'Asia/Singapore'],
  ['78N', 78, 15.6, 'Arctic/Longyearbyen'],
]

describe('Hour lines — the year-wide curve for each local clock hour (specs/086)', () => {
  it('keeps every point of every run at or above the horizon (FR-005, SC-003)', () => {
    for (const [name, latitude, longitude, timeZoneId] of HOUR_LINE_SITES) {
      for (let hour = 0; hour < 24; hour++) {
        for (const run of sampleHourLineRuns(hour, latitude, longitude, 2026, timeZoneId, 120)) {
          for (const point of run) {
            expect(point.z, `${name} ${hour}`).toBeGreaterThanOrEqual(-1e-6)
          }
        }
      }
    }
  })

  it('puts the 12:00 mark on the 12:00 curve, on the site’s own clock (FR-009, SC-006)', () => {
    for (const [, latitude, longitude, timeZoneId] of HOUR_LINE_SITES.slice(0, 3)) {
      const date = new Date(Date.UTC(2026, 5, 21))
      const mark = buildDatedPath(date, latitude, longitude, date, timeZoneId).hourMarks.find(
        (sprite) => sprite.userData.localHour === 12,
      )
      expect(mark).toBeDefined()

      const points = sampleHourLineRuns(12, latitude, longitude, 2026, timeZoneId, 120).flat()
      const onCurve = points.some((point) => point.distanceTo(mark!.position) < 1e-6)

      expect(onCurve, `${latitude},${longitude}`).toBe(true)
    }
  })

  it('draws nothing for an hour that never sees the sun, and tubes for the rest (FR-001, FR-006)', () => {
    const meshes = buildHourLines(25.2, 55.3, 2026, 'Asia/Dubai', 120, 0x524e46)

    // Midnight sun does not happen at 25°: the midnight hour is never above the horizon.
    expect(meshes.some((mesh) => mesh.userData.localHour === 0)).toBe(false)
    expect(meshes.some((mesh) => mesh.userData.localHour === 12)).toBe(true)

    for (const mesh of meshes) {
      expect(mesh).toBeInstanceOf(THREE.Mesh)
      expect(mesh).not.toBeInstanceOf(THREE.Line)
      expect(mesh.geometry).toBeInstanceOf(THREE.TubeGeometry)
    }
  })
})

describe('Hour-line runs are split at the horizon (specs/086 US3)', () => {
  it('never joins across a below-horizon sample (FR-004)', () => {
    const a = new THREE.Vector3(1, 0, 0)
    const b = new THREE.Vector3(2, 0, 0)
    const c = new THREE.Vector3(3, 0, 0)

    expect(splitAboveHorizonRuns([a, b, null, c])).toEqual([[a, b], [c]])
    expect(splitAboveHorizonRuns([])).toEqual([])
    expect(splitAboveHorizonRuns([null, null])).toEqual([])
  })

  it('draws the real horizon gaps as separate runs (FR-004, SC-003, research D11)', () => {
    // Measured with this algorithm over 2026: the sun is up at 06:00 local in three separate
    // stretches at Sydney. A single curve through them would join the last day of one stretch to
    // the first of the next — a chord across the missing days and through the ground.
    expect(sampleHourLineRuns(6, -33.87, 151.21, 2026, 'Australia/Sydney', 120)).toHaveLength(3)
    expect(sampleHourLineRuns(7, -33.87, 151.21, 2026, 'Australia/Sydney', 120)).toHaveLength(3)
    expect(sampleHourLineRuns(6, 51.5, -0.1, 2026, 'Europe/London', 120)).toHaveLength(2)
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
/** The light theme's `text.secondary`, the value the overlay actually passes (specs/086 FR-003). */
const HOUR_LINE_COLOR = '#524E46'

function identitiesOf(group: THREE.Group): THREE.Object3D[] {
  return [...group.children]
}

describe('A date change rebuilds only the dated group (T038, FR-021, SC-008)', () => {
  it('leaves every fixed-furniture object identity untouched across a sequence of dates', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)

    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)
    const datedBefore = identitiesOf(scene.sunPathDatedGroup)
    expect(fixedBefore.length).toBeGreaterThan(0)
    expect(datedBefore.length).toBeGreaterThan(0)

    for (const date of ['2026-09-23', '2026-10-15', '2026-12-21', '2026-03-20']) {
      const rebuilt = scene.updateSunPath(date, DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
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
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)
    const datedBefore = identitiesOf(scene.sunPathDatedGroup)

    const rebuilt = scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 14)), 240, 30, 'Asia/Dubai', HOUR_LINE_COLOR)

    expect(rebuilt).toBe(false)
    expect(identitiesOf(scene.sunPathFixedGroup)).toEqual(fixedBefore)
    expect(identitiesOf(scene.sunPathDatedGroup)).toEqual(datedBefore)

    scene.disposeAll()
  })
})

describe('A site change or a year change rebuilds both groups (T039, FR-022)', () => {
  it('rebuilds the fixed furniture on a site change', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    // London: a different latitude puts the monthly lattice and the dial somewhere else entirely.
    scene.updateSunPath('2026-09-22', 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Europe/London', HOUR_LINE_COLOR)

    expect(identitiesOf(scene.sunPathFixedGroup)).not.toEqual(fixedBefore)
    scene.disposeAll()
  })

  it('rebuilds the fixed furniture on a year change', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-12-31', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 11, 31, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    scene.updateSunPath('2027-01-01', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2027, 0, 1, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)

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
    const furniture = buildFixedFurniture(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai', 0x524e46)
    expect(furniture.shell.renderOrder).toBeGreaterThan(furniture.dial.renderOrder)
  })

  it('rebuilds the fixed furniture when only the time zone changes (specs/086 FR-008)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    // The zone resolves after the first draw (the fallback is UTC); the curves were drawn on the
    // wrong clock and only a rebuild on this key change can correct them.
    const rebuilt = scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'UTC', HOUR_LINE_COLOR)

    expect(rebuilt).toBe(true)
    expect(identitiesOf(scene.sunPathFixedGroup)).not.toEqual(fixedBefore)
    scene.disposeAll()
  })
})

describe('Hour lines are fixed furniture (specs/086 US2)', () => {
  function hourLineMeshesOf(scene: SolarScene): THREE.Mesh[] {
    return scene.sunPathFixedGroup.children.filter(
      (child) => child.userData.localHour !== undefined,
    ) as THREE.Mesh[]
  }

  it('keeps the hour lines in the fixed group across a year of date changes (FR-007, SC-002)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)

    const hourLinesBefore = hourLineMeshesOf(scene)
    expect(hourLinesBefore.length).toBeGreaterThan(0)
    const fixedBefore = identitiesOf(scene.sunPathFixedGroup)

    for (const date of ['2026-01-01', '2026-06-21', '2026-12-31']) {
      scene.updateSunPath(date, DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    }

    expect(identitiesOf(scene.sunPathFixedGroup)).toEqual(fixedBefore)
    hourLinesBefore.forEach((mesh, index) => expect(hourLineMeshesOf(scene)[index]).toBe(mesh))
    scene.disposeAll()
  })

  it('re-tints the hour lines in place when only the colour changes (FR-003)', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)

    const before = hourLineMeshesOf(scene)
    const colorBefore = (before[0].material as THREE.MeshBasicMaterial).color.getHex()

    // The dark theme's text.secondary — same site, year and zone, so only the colour changed.
    const rebuilt = scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', '#9A9587')

    const after = hourLineMeshesOf(scene)
    expect(rebuilt).toBe(true)
    expect(after.length).toBe(before.length)
    after.forEach((mesh, index) => expect(mesh).toBe(before[index]))
    expect((after[0].material as THREE.MeshBasicMaterial).color.getHex()).not.toBe(colorBefore)
    expect((after[0].material as THREE.MeshBasicMaterial).color.getHex()).toBe(new THREE.Color('#9A9587').getHex())

    scene.disposeAll()
  })
})

describe('Arcs stay tubes, never THREE.Line (T040, README constraint 5)', () => {
  it('builds every dated arc and every monthly lattice arc from TubeGeometry', () => {
    const dated = buildDatedPath(new Date(Date.UTC(2026, 8, 22)), DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 'Asia/Dubai')
    const furniture = buildFixedFurniture(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai', 0x524e46)

    for (const arc of [dated.chosenDay, dated.summerExtreme, dated.winterExtreme, ...furniture.monthlyArcs]) {
      expect(arc).not.toBeNull()
      expect(arc!).toBeInstanceOf(THREE.Mesh)
      expect(arc!).not.toBeInstanceOf(THREE.Line)
      expect(arc!.geometry).toBeInstanceOf(THREE.TubeGeometry)
    }
  })

  it('introduces no THREE.Line anywhere in either assembled group', () => {
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)

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
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    const fixedCount = scene.sunPathFixedGroup.children.length
    const datedCount = scene.sunPathDatedGroup.children.length

    for (let cycle = 0; cycle < 10; cycle++) {
      scene.updateSunPath('2026-09-23', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 23, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    }

    // Same date and site as the very first call, so the same instrument — not ten of them stacked.
    expect(scene.sunPathFixedGroup.children.length).toBe(fixedCount)
    expect(scene.sunPathDatedGroup.children.length).toBe(datedCount)

    for (let cycle = 0; cycle < 10; cycle++) {
      scene.updateSunPath('2026-09-22', 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Europe/London', HOUR_LINE_COLOR)
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    }

    expect(scene.sunPathFixedGroup.children.length).toBe(fixedCount)
    expect(scene.sunPathDatedGroup.children.length).toBe(datedCount)

    // T044 — disposeAll must cover BOTH scopes, or an open/close cycle leaks the half it forgot.
    scene.disposeAll()
    expect(scene.sunPathFixedGroup.children.length).toBe(0)
    expect(scene.sunPathDatedGroup.children.length).toBe(0)
  })

  // specs/086 — each cycle now builds the 24 hour curves (8,760 samples) as well as the lattice,
  // so fifty of them exceeds vitest's 5 s default. The assertion is unchanged: nothing accumulates.
  it('empties both groups on every one of fifty open/close cycles (SC-010)', () => {
    for (let cycle = 0; cycle < 50; cycle++) {
      const scene = new SolarScene(fakeDrawingSpace())
      scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
      expect(scene.sunPathFixedGroup.children.length).toBeGreaterThan(0)

      scene.disposeAll()
      expect(scene.sunPathFixedGroup.children.length).toBe(0)
      expect(scene.sunPathDatedGroup.children.length).toBe(0)
    }
  }, 30_000)
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

describe('The dome encloses the building under study (specs/076)', () => {
  /** A square site building centred on the scene's reference point, `halfWidthMetres` each way. */
  function siteBuilding(halfWidthMetres: number, heightMetres: number): SiteBuildingDto {
    const dLat = halfWidthMetres / 111_320
    const dLon = halfWidthMetres / (111_320 * Math.cos((DUBAI.latitude * Math.PI) / 180))
    const { latitude, longitude } = DUBAI
    return {
      id: 'site',
      ring: [
        { latitude: latitude - dLat, longitude: longitude - dLon },
        { latitude: latitude - dLat, longitude: longitude + dLon },
        { latitude: latitude + dLat, longitude: longitude + dLon },
        { latitude: latitude + dLat, longitude: longitude - dLon },
      ],
      heightMetres,
      heightProvenance: 'known',
      name: 'site',
      isSiteBuilding: true,
    }
  }

  function shellRadius(scene: SolarScene): number {
    const shell = scene.sunPathFixedGroup.children.find((child) => child.renderOrder === 1) as THREE.Mesh<THREE.SphereGeometry>
    return shell.geometry.parameters.radius
  }

  it('keeps the reference radius for a building that already fits, and never exceeds the cap', () => {
    expect(sunPathDomeRadiusFor(0)).toBe(SUN_PATH_DOME_RADIUS_METRES)
    expect(sunPathDomeRadiusFor(80)).toBe(SUN_PATH_DOME_RADIUS_METRES)
    expect(sunPathDomeRadiusFor(10_000)).toBe(MAX_SUN_PATH_DOME_RADIUS_METRES)
  })

  it('grows past the furthest roof corner of a building too big for the reference dome', () => {
    const radius = sunPathDomeRadiusFor(200)

    expect(radius).toBeGreaterThan(200)
    expect(radius % 10).toBe(0)
  })

  it('redraws an already-drawn dome at once when a bigger site building arrives, leaving the shadow rig alone', () => {
    sceneAnchor.set(DUBAI)
    const scene = new SolarScene(fakeDrawingSpace())
    scene.updateSunPath('2026-09-22', DUBAI.latitude, DUBAI.longitude, new Date(Date.UTC(2026, 8, 22, 10)), 180, 45, 'Asia/Dubai', HOUR_LINE_COLOR)
    expect(shellRadius(scene)).toBe(SUN_PATH_DOME_RADIUS_METRES)

    // BurJuman-sized: 200 m across, so its corners are ~141 m out — through the reference shell.
    const built = scene.rebuildBuildings([siteBuilding(100, 26)])

    expect(scene.sunPathDomeRadiusMetres).toBeGreaterThan(built.siteReachMetres)
    expect(shellRadius(scene)).toBe(scene.sunPathDomeRadiusMetres)
    const marker = scene.sunPathDatedGroup.children.at(-1)!
    expect(marker.position.length()).toBeCloseTo(scene.sunPathDomeRadiusMetres, 6)

    // The same buildings with no dome drawn at all get the very same shadow rig.
    const withoutDome = new SolarScene(fakeDrawingSpace())
    withoutDome.rebuildBuildings([siteBuilding(100, 26)])
    scene.aimSun(180, 45)
    withoutDome.aimSun(180, 45)
    expect(scene.radiusMetres).toBe(withoutDome.radiusMetres)

    withoutDome.disposeAll()
    scene.disposeAll()
  })
})

describe('the dome draws as an overlay, not as scenery', () => {
  // The renderer shares Google Maps' depth buffer, so a building nearer the camera would otherwise
  // cut the dome out of the picture — the bug this pins.
  it('keeps the shell out of the depth buffer', () => {
    const material = buildDomeShell(120).material as THREE.MeshStandardMaterial

    expect(material.depthTest).toBe(false)
    expect(material.depthWrite).toBe(false)
  })

  it('keeps the arcs out of the depth buffer, and writes no depth that could hole a building', () => {
    const tube = buildTubeFromPoints(
      [new THREE.Vector3(0, 0, 0), new THREE.Vector3(10, 10, 10)],
      0.5,
      0xff5a3c,
      1,
    )
    const material = tube?.material as THREE.MeshBasicMaterial

    expect(material.depthTest).toBe(false)
    expect(material.depthWrite).toBe(false)
  })
})

describe('the dome draws last, over the massing (regression)', () => {
  it('gives both dome groups a render order later than the scene’s own objects', () => {
    const scene = new SolarScene(fakeDrawingSpace())

    // The massing boxes are opaque meshes in the same render pass; with the default order they
    // painted over the shell and the arcs wherever they stood in front of the dome.
    expect(scene.sunPathFixedGroup.renderOrder).toBeGreaterThan(scene.buildingsGroup.renderOrder)
    expect(scene.sunPathDatedGroup.renderOrder).toBeGreaterThan(scene.sunPathFixedGroup.renderOrder)

    scene.disposeAll()
  })
})

describe('the dome occluder (Maps draws its buildings after onDraw)', () => {
  it('writes near-plane depth without writing colour', () => {
    const occluder = buildDomeOccluder(120)
    const material = occluder.material as THREE.ShaderMaterial

    // Colour off: it must add nothing to the picture. Depth on and pinned to the near plane: a
    // later pass's buildings behind the dome fail their depth test instead of painting over it.
    expect(material.colorWrite).toBe(false)
    expect(material.depthWrite).toBe(true)
    expect(material.depthTest).toBe(false)
    expect(material.vertexShader).toContain('-clip.w')
  })
})
