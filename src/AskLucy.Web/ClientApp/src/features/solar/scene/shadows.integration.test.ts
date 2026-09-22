import * as THREE from 'three'
import { beforeEach, describe, expect, it } from 'vitest'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { buildFootprintMesh } from '../buildings/footprintGeometry'
import { copy } from '../copy'
import { buildSolarFiguresContent } from '../panels/solarFiguresContent'
import { daySummary } from '../solar/daySummary'
import { geometricAltitudeForApparentDegrees, HORIZON_REFRACTION_DEGREES } from '../solar/refraction'
import { solarPosition, solarPositionToEnuUnitVector, SOLAR_POSITION_TOLERANCE_DEGREES } from '../solar/solarPosition'
import { SunLight } from './sunLight'

/**
 * specs/052-solar-analysis T045, FR-016, FR-017, US2 scenario 3 — this test suite cannot render
 * actual pixels (no GPU in this environment, consistent with the quickstart's own "not measured
 * in this environment" policy for shadow-pixel correctness). What IS asserted here is the
 * structural mechanism that makes both FR-016 requirements possible: every building mesh carries
 * both `castShadow` and `receiveShadow`, which is what allows a taller building's shadow to land
 * on a shorter neighbour in three.js's shadow-map pass — and that the light is fully disabled
 * (casting nothing) when the sun is below the horizon, with that fact stated in the figures.
 */

const TALL_BUILDING: SiteBuildingDto = {
  id: 'osm_way_tall',
  ring: [
    { latitude: 25.1565, longitude: 55.2210 },
    { latitude: 25.1565, longitude: 55.2213 },
    { latitude: 25.1562, longitude: 55.2213 },
    { latitude: 25.1562, longitude: 55.2210 },
  ],
  heightMetres: 80,
  heightProvenance: 'known',
  name: 'Tall Tower',
  isSiteBuilding: false,
}

const SHORT_NEIGHBOUR: SiteBuildingDto = {
  id: 'osm_way_short',
  ring: [
    { latitude: 25.1560, longitude: 55.2216 },
    { latitude: 25.1560, longitude: 55.2219 },
    { latitude: 25.1557, longitude: 55.2219 },
    { latitude: 25.1557, longitude: 55.2216 },
  ],
  heightMetres: 10,
  heightProvenance: 'known',
  name: 'Short Neighbour',
  isSiteBuilding: true,
}

beforeEach(() => {
  sceneAnchor.set({ latitude: 25.156, longitude: 55.2215 })
})

describe('Inter-building shadows — structural mechanism (FR-016 "onto the ground and onto each other")', () => {
  it('every building mesh both casts AND receives shadows, which is what allows one to shadow another', () => {
    const tallMesh = buildFootprintMesh(TALL_BUILDING, false)!
    const shortMesh = buildFootprintMesh(SHORT_NEIGHBOUR, false)!

    for (const mesh of [tallMesh, shortMesh]) {
      expect(mesh.castShadow).toBe(true)
      expect(mesh.receiveShadow).toBe(true)
    }
  })

  it('a taller building extrudes to a greater depth than a shorter neighbour (the geometric precondition for it to cast further/higher)', () => {
    const tallMesh = buildFootprintMesh(TALL_BUILDING, false)!
    const shortMesh = buildFootprintMesh(SHORT_NEIGHBOUR, false)!

    const tallDepth = (tallMesh.geometry as THREE.ExtrudeGeometry).parameters.options.depth as number
    const shortDepth = (shortMesh.geometry as THREE.ExtrudeGeometry).parameters.options.depth as number
    expect(tallDepth).toBeGreaterThan(shortDepth)
  })
})

describe('No shadows when the sun is below the horizon (FR-017, US2 scenario 3)', () => {
  it('disables the light entirely (zero intensity, invisible) when altitude <= 0', () => {
    const light = new SunLight(200)
    light.aimAt(180, -5)
    expect(light.directionalLight.visible).toBe(false)
    expect(light.directionalLight.intensity).toBe(0)
  })

  it('states the below-horizon condition in the figures document, not silently', () => {
    const nightInstant = new Date(Date.UTC(2026, 8, 13, 0, 0)) // deep night at Dubai
    const content = buildSolarFiguresContent({
      localDate: '2026-09-13',
      localMinuteOfDay: 4 * 60,
      timeZoneId: 'Asia/Dubai',
      timeBasisLabel: 'Asia/Dubai',
      solarPosition: solarPosition(nightInstant, 25.2, 55.3),
      daySummary: daySummary(nightInstant, 25.2, 55.3),
      siteBuildingHeightAssumed: null,
    })
    const textBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }
    expect(textBlock.text).toContain(copy.belowHorizonNotice)
  })
})

/**
 * T021, FR-027, SC-009 — the one sanctioned reason a shadow may sit anywhere other than where the
 * previous release put it is the refraction correction FR-007 requires; nothing in this release's
 * performance or frustum work may move a shadow for any other reason.
 *
 * The previous release aimed the light at the *geometric* altitude. `geometricAltitudeForApparent`
 * recovers exactly that baseline from the apparent altitude now reported, so the two light
 * directions can be compared directly — the angle between them IS the refraction correction, and
 * asserting it bounds the shadow movement without needing a rendered pixel.
 */
function angleBetweenDegrees(a: { x: number; y: number; z: number }, b: { x: number; y: number; z: number }): number {
  const dot = Math.min(1, Math.max(-1, a.x * b.x + a.y * b.y + a.z * b.z))
  return (Math.acos(dot) * 180) / Math.PI
}

describe('Shadow direction vs the previous release (T021, FR-027, SC-009)', () => {
  const AZIMUTH = 137.4

  it('is unchanged, within the stated position tolerance, above 15° elevation', () => {
    for (const apparentAltitude of [15, 20, 30, 45, 60, 75, 89]) {
      const current = solarPositionToEnuUnitVector(AZIMUTH, apparentAltitude)
      const baseline = solarPositionToEnuUnitVector(AZIMUTH, geometricAltitudeForApparentDegrees(apparentAltitude))

      expect(angleBetweenDegrees(current, baseline)).toBeLessThanOrEqual(SOLAR_POSITION_TOLERANCE_DEGREES)
    }
  })

  it('differs near the horizon by no more than the refraction correction itself — about half a degree', () => {
    for (const apparentAltitude of [0, 0.5, 1, 2, 5, 10]) {
      const current = solarPositionToEnuUnitVector(AZIMUTH, apparentAltitude)
      const baseline = solarPositionToEnuUnitVector(AZIMUTH, geometricAltitudeForApparentDegrees(apparentAltitude))

      expect(angleBetweenDegrees(current, baseline)).toBeLessThanOrEqual(HORIZON_REFRACTION_DEGREES + 1e-9)
    }
    expect(HORIZON_REFRACTION_DEGREES).toBeLessThan(0.6)
  })

  it('moves the shadow only vertically: the compass bearing a shadow falls along is untouched by refraction (FR-005)', () => {
    const apparentAltitude = 3
    const current = solarPositionToEnuUnitVector(AZIMUTH, apparentAltitude)
    const baseline = solarPositionToEnuUnitVector(AZIMUTH, geometricAltitudeForApparentDegrees(apparentAltitude))

    // The shadow's ground bearing is the horizontal component of the light direction, negated.
    const currentBearing = Math.atan2(current.x, current.y)
    const baselineBearing = Math.atan2(baseline.x, baseline.y)
    expect(currentBearing).toBeCloseTo(baselineBearing, 10)
  })
})
