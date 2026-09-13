import { describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { FALLBACK_TIME_ZONE, fromLocalParts, resolveTimeZone, toLocalParts } from './timeZone'

describe('resolveTimeZone (research D2, FR-003)', () => {
  it('resolves a known IANA zone for Dubai', async () => {
    const basis = await resolveTimeZone(25.2, 55.3)
    expect(basis.timeZoneId).toBe('Asia/Dubai')
    expect(basis.timeBasisLabel).toBe('Asia/Dubai')
  })

  it('resolves a known IANA zone for London', async () => {
    const basis = await resolveTimeZone(51.5, -0.1)
    expect(basis.timeZoneId).toBe('Europe/London')
  })

  it('states the time basis explicitly, never silently, when it cannot be determined', async () => {
    // Invalid coordinates are what actually makes the underlying lookup fail (verified against
    // the installed tz-lookup package, which throws rather than returning null) — exercising the
    // same "undetermined" path FR-003 requires be stated rather than assumed.
    const basis = await resolveTimeZone(Number.NaN, 0)
    expect(basis.timeZoneId).toBeNull()
    expect(basis.timeBasisLabel).toBe(copy.timeBasisUndetermined)
    expect(basis.timeBasisLabel.toLowerCase()).toContain('utc')
    expect(basis.timeBasisLabel).not.toBe('UTC') // never a bare label that reads as if determined
  })
})

describe('toLocalParts / fromLocalParts round-trip', () => {
  it('round-trips a plain instant through a fixed-offset zone', () => {
    const instant = new Date(Date.UTC(2026, 5, 15, 10, 30))
    const { localDate, localMinuteOfDay } = toLocalParts(instant, 'Asia/Dubai') // UTC+4, no DST
    expect(localDate).toBe('2026-06-15')
    expect(localMinuteOfDay).toBe(14 * 60 + 30)

    const roundTripped = fromLocalParts(localDate, localMinuteOfDay, 'Asia/Dubai')
    expect(roundTripped.getTime()).toBe(instant.getTime())
  })

  it('resolves through the fallback zone when the time basis is undetermined', () => {
    const instant = new Date(Date.UTC(2026, 0, 1, 6, 0))
    const { localDate, localMinuteOfDay } = toLocalParts(instant, FALLBACK_TIME_ZONE)
    expect(localDate).toBe('2026-01-01')
    expect(localMinuteOfDay).toBe(6 * 60)
  })
})

describe('daylight-saving transitions (SC-005, FR-004)', () => {
  it('stays correct across London spring-forward (clocks go forward at 01:00 UTC, last Sunday of March)', () => {
    // 2026-03-29 01:00 UTC is the instant BST begins; local time jumps from 01:00 to 02:00.
    const beforeTransition = new Date(Date.UTC(2026, 2, 29, 0, 30)) // 00:30 GMT
    const afterTransition = new Date(Date.UTC(2026, 2, 29, 1, 30)) // 02:30 BST

    expect(toLocalParts(beforeTransition, 'Europe/London').localMinuteOfDay).toBe(0 * 60 + 30)
    expect(toLocalParts(afterTransition, 'Europe/London').localMinuteOfDay).toBe(2 * 60 + 30)

    // Driving instantUtc forward by exactly one hour across the transition, the local minute
    // jumps by two hours — the gap is real and never silently smoothed over.
    const oneHourLaterUtc = new Date(beforeTransition.getTime() + 60 * 60_000)
    expect(oneHourLaterUtc.getTime()).toBe(afterTransition.getTime())
  })

  it('stays correct across London autumn-back (clocks go back at 02:00 BST, last Sunday of October)', () => {
    // 2026-10-25 01:00 UTC is the instant BST ends; the local hour 01:00-02:00 occurs twice.
    const firstOneAm = new Date(Date.UTC(2026, 9, 25, 0, 30)) // 01:30 BST (still summer time)
    const secondOneAm = new Date(Date.UTC(2026, 9, 25, 1, 30)) // 01:30 GMT (after fall-back)

    expect(toLocalParts(firstOneAm, 'Europe/London').localMinuteOfDay).toBe(1 * 60 + 30)
    expect(toLocalParts(secondOneAm, 'Europe/London').localMinuteOfDay).toBe(1 * 60 + 30)

    // The two distinct UTC instants both format to the same wall-clock local time — that
    // ambiguity is real (the fold), and fromLocalParts resolves it to exactly one answer
    // (whichever the browser's own tzdata converges to), never leaving it unresolved.
    const resolved = fromLocalParts('2026-10-25', 1 * 60 + 30, 'Europe/London')
    expect(resolved.getTime()).not.toBeNaN()
  })

  it('stays correct across a southern-hemisphere transition (Sydney spring-forward, October)', () => {
    // Australia/Sydney springs forward at 02:00 AEST -> 03:00 AEDT, first Sunday of October.
    const before = new Date(Date.UTC(2026, 9, 3, 15, 30)) // 02:30 AEST on Oct 4 local
    const after = new Date(Date.UTC(2026, 9, 3, 16, 30)) // 03:30 AEDT on Oct 4 local

    const beforeParts = toLocalParts(before, 'Australia/Sydney')
    const afterParts = toLocalParts(after, 'Australia/Sydney')
    expect(afterParts.localMinuteOfDay - beforeParts.localMinuteOfDay).toBe(120)
  })
})
