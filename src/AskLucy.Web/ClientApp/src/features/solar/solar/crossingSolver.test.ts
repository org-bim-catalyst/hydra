import { describe, expect, it } from 'vitest'
import {
  CROSSING_SOLVER_MAX_ITERATIONS,
  cosHourAngle,
  crossingSeedMs,
  solveCrossingMs,
} from './crossingSolver'
import { daySummary } from './daySummary'
import { RISE_SET_GEOMETRIC_ALTITUDE_DEGREES, SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import { solarPosition } from './solarPosition'

/** The quantity `daySummary` drives to zero, reproduced here so the solver can be checked against
 * it without importing a private function: the apparent altitude of the sun's upper edge. */
function upperEdgeAltitude(instantMs: number, latitude: number, longitude: number): number {
  return solarPosition(new Date(instantMs), latitude, longitude).altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES
}

/** The seed `daySummary` starts from, built from the same closed form it uses. */
function riseSetSeed(dateUtc: Date, latitude: number, longitude: number, direction: 'morning' | 'evening'): number {
  const noonReference = new Date(Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 12, 0, 0))
  const { declination, eqTime } = solarPosition(noonReference, latitude, longitude)
  const cosH0 = cosHourAngle(latitude, declination, RISE_SET_GEOMETRIC_ALTITUDE_DEGREES)
  const hourAngle = (Math.acos(cosH0) * 180) / Math.PI
  const dayStartUtc = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 0, 0, 0)
  return crossingSeedMs(dayStartUtc, longitude, eqTime, hourAngle, direction)
}

describe('solveCrossingMs', () => {
  const target = Date.UTC(2026, 8, 25, 6, 12, 34)

  it('converges on the instant at which the quantity reaches zero', () => {
    const solved = solveCrossingMs(target - 3_600_000, (ms) => ms - target)

    expect(solved).toBe(target)
  })

  it('never returns the seed it was given when it cannot converge', () => {
    const seed = target - 3_600_000

    // `maxIterations: 0` denies the solver any evaluation, which is the existing seam
    // `daySummary.test.ts` uses for its own non-convergence case.
    expect(solveCrossingMs(seed, (ms) => ms - target, 0)).toBeNull()
  })

  it('reports no answer when the slope vanishes — a grazing crossing has no determined instant', () => {
    // A flat quantity: Newton has nothing to descend, and the answer is genuinely undetermined
    // rather than merely slow to find.
    expect(solveCrossingMs(target, () => 0.5)).toBeNull()
  })

  it('returns null rather than a non-finite instant when the slope throws the iterate away', () => {
    let calls = 0
    const solved = solveCrossingMs(target, (ms) => {
      calls += 1
      // First evaluation is finite; the slope evaluation after it divides by zero.
      return calls === 1 ? 0.5 : Number.POSITIVE_INFINITY * (ms - target)
    })

    expect(solved).toBeNull()
  })

  it('stops at the iteration cap on a quantity that converges too slowly', () => {
    // Newton on a cubic closes only two thirds of the gap per step: it needs tens of iterations
    // to reach 1e-5. With a cap of two it must say so rather than return where it got to.
    expect(solveCrossingMs(target - 3_600_000, (ms) => (ms - target) ** 3 * 1e-15, 2)).toBeNull()
  })

  it('uses the documented iteration cap by default', () => {
    expect(CROSSING_SOLVER_MAX_ITERATIONS).toBe(12)
  })
})

describe('cosHourAngle', () => {
  it('is within the solvable range when the sun reaches the target altitude', () => {
    const cosH0 = cosHourAngle(25.2, 0, -6)

    expect(cosH0).toBeGreaterThan(-1)
    expect(cosH0).toBeLessThan(1)
  })

  it('exceeds 1 when the sun stays below the target all day', () => {
    // 80° N in midwinter: the sun never climbs to 6° below the horizon.
    expect(cosHourAngle(80, -23.4, -6)).toBeGreaterThan(1)
  })

  it('falls below -1 when the sun stays above the target all day', () => {
    // 80° N in midsummer: the sun never descends to 6° below the horizon.
    expect(cosHourAngle(80, 23.4, -6)).toBeLessThan(-1)
  })
})

describe('crossingSeedMs', () => {
  it('seeds the morning before solar noon and the evening after it', () => {
    const dayStart = Date.UTC(2026, 8, 25)
    const solarNoonMinutes = 720 - 4 * 55.27

    const morning = crossingSeedMs(dayStart, 55.27, 0, 90, 'morning')
    const evening = crossingSeedMs(dayStart, 55.27, 0, 90, 'evening')

    expect(morning).toBe(dayStart + (solarNoonMinutes - 90 * 4) * 60_000)
    expect(evening).toBe(dayStart + (solarNoonMinutes + 90 * 4) * 60_000)
  })
})

// specs/085 research D4 — the solver was extracted from `daySummary` so the twilight thresholds
// can reuse it. This is the proof the extraction changed nothing: the shared solver, given the
// same seed and the same quantity, lands on exactly the instant `daySummary` reports.
describe('the shared solver reproduces daySummary’s rise and set', () => {
  const cases = [
    { name: 'Dubai', latitude: 25.2048, longitude: 55.2708 },
    { name: 'London', latitude: 51.5074, longitude: -0.1278 },
    { name: 'Singapore', latitude: 1.3521, longitude: 103.8198 },
    { name: 'Tromsø', latitude: 69.6492, longitude: 18.9553 },
  ]
  const dates = [new Date(Date.UTC(2026, 8, 25)), new Date(Date.UTC(2026, 11, 21)), new Date(Date.UTC(2026, 5, 21))]

  for (const site of cases) {
    for (const date of dates) {
      it(`${site.name} on ${date.toISOString().slice(0, 10)}`, () => {
        const summary = daySummary(date, site.latitude, site.longitude)
        if (summary.polarCondition !== 'none') return

        const sunrise = solveCrossingMs(
          riseSetSeed(date, site.latitude, site.longitude, 'morning'),
          (ms) => upperEdgeAltitude(ms, site.latitude, site.longitude),
        )
        const sunset = solveCrossingMs(
          riseSetSeed(date, site.latitude, site.longitude, 'evening'),
          (ms) => upperEdgeAltitude(ms, site.latitude, site.longitude),
        )

        expect(sunrise).toBe(summary.sunriseUtc!.getTime())
        expect(sunset).toBe(summary.sunsetUtc!.getTime())
      })
    }
  }
})
