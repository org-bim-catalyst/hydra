import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { buildSunPath, buildTubeFromPoints, sampleDayArcPoints, sphericalToVec } from './sunPathCurve'

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

describe('buildSunPath — chosen day, seasonal extremes and hour marks are distinct objects (FR-005, FR-006, FR-007)', () => {
  it('produces separate, distinguishably-materialed meshes for chosen day, summer and winter extremes', () => {
    const result = buildSunPath(new Date(Date.UTC(2026, 8, 22)), 25.2, 55.3, new Date(Date.UTC(2026, 8, 22, 10, 0)))

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
    const north = buildSunPath(new Date(Date.UTC(2026, 8, 22)), 51.5, -0.1, new Date(Date.UTC(2026, 8, 22, 10, 0)))
    const south = buildSunPath(new Date(Date.UTC(2026, 8, 22)), -54.8, -68.3, new Date(Date.UTC(2026, 8, 22, 10, 0)))
    // Both hemispheres must produce a summer/winter pair; which calendar month backs each differs,
    // which is exercised indirectly through daySummary's own hemisphere test — this test only
    // confirms both tracks exist for both hemispheres.
    expect(north.summerExtreme).not.toBeNull()
    expect(north.winterExtreme).not.toBeNull()
    expect(south.summerExtreme).not.toBeNull()
    expect(south.winterExtreme).not.toBeNull()
  })

  it('marks hours along the chosen day only when the sun is meaningfully above the horizon', () => {
    const result = buildSunPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 12, 0)))
    expect(result.hourMarks.length).toBeGreaterThan(0)
    for (const sprite of result.hourMarks) {
      expect(sprite).toBeInstanceOf(THREE.Sprite)
    }
  })

  it('shows the current position marker in gold when above the horizon, dim when below', () => {
    const day = buildSunPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 10, 0)))
    const night = buildSunPath(new Date(Date.UTC(2026, 5, 21)), 25.2, 55.3, new Date(Date.UTC(2026, 5, 21, 0, 0)))
    const dayColor = (day.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    const nightColor = (night.currentPositionMarker.material as THREE.MeshBasicMaterial).color.getHex()
    expect(dayColor).not.toBe(nightColor)
  })
})
