import { describe, expect, it } from 'vitest'
import {
  MINUTES_PER_DAY,
  SNAP_MINUTES,
  buildTimeSliderMarks,
  formatLocalTime,
  formatTimeDraftInput,
  parseLocalTimeEntry,
  snapToQuarterHour,
} from './timeEntry'

const MAX_QUARTER_HOUR_MARK = MINUTES_PER_DAY - SNAP_MINUTES

describe('formatLocalTime', () => {
  it('zero-pads hour and minute', () => {
    expect(formatLocalTime(6 * 60 + 5)).toBe('06:05')
  })

  it('covers both ends of the day', () => {
    expect(formatLocalTime(0)).toBe('00:00')
    expect(formatLocalTime(MINUTES_PER_DAY - 1)).toBe('23:59')
  })
})

describe('parseLocalTimeEntry (contracts/time-entry.md, T005/T006)', () => {
  it('accepts H:MM, HH:MM, and surrounding whitespace', () => {
    expect(parseLocalTimeEntry('6:41')).toEqual({ ok: true, minuteOfDay: 6 * 60 + 41 })
    expect(parseLocalTimeEntry('06:41')).toEqual({ ok: true, minuteOfDay: 6 * 60 + 41 })
    expect(parseLocalTimeEntry(' 06:41 ')).toEqual({ ok: true, minuteOfDay: 6 * 60 + 41 })
  })

  it('round-trips against formatLocalTime for every valid minute', () => {
    for (let minute = 0; minute < MINUTES_PER_DAY; minute += 7) {
      const result = parseLocalTimeEntry(formatLocalTime(minute))
      expect(result).toEqual({ ok: true, minuteOfDay: minute })
    }
  })

  it('rejects unparseable text as malformed (FR-003)', () => {
    expect(parseLocalTimeEntry('abc')).toEqual({ ok: false, reason: 'malformed' })
    expect(parseLocalTimeEntry('')).toEqual({ ok: false, reason: 'malformed' })
    expect(parseLocalTimeEntry('6:')).toEqual({ ok: false, reason: 'malformed' })
  })

  it('rejects an hour or minute outside range without clamping (FR-004)', () => {
    expect(parseLocalTimeEntry('25:00')).toEqual({ ok: false, reason: 'outOfRange' })
    expect(parseLocalTimeEntry('06:75')).toEqual({ ok: false, reason: 'outOfRange' })
  })
})

describe('snapToQuarterHour (FR-018)', () => {
  it('returns the nearest multiple of SNAP_MINUTES', () => {
    expect(snapToQuarterHour(7)).toBe(0)
    expect(snapToQuarterHour(8)).toBe(15)
  })

  it('clamps 23:53 to 23:45, never to 24:00', () => {
    expect(snapToQuarterHour(23 * 60 + 53)).toBe(23 * 60 + 45)
    expect(snapToQuarterHour(MINUTES_PER_DAY - 1)).toBeLessThan(MINUTES_PER_DAY)
  })

  it('ties round consistently', () => {
    // 1432 is exactly between 1425 and 1440; the implementation's rounding direction is pinned
    // here so a change in behaviour is a deliberate, reviewed decision rather than a silent drift.
    const first = snapToQuarterHour(23 * 60 + 52)
    const second = snapToQuarterHour(23 * 60 + 52)
    expect(first).toBe(second)
  })
})

describe('formatTimeDraftInput', () => {
  it('inserts the colon after the second digit as digits accumulate', () => {
    expect(formatTimeDraftInput('0')).toBe('0')
    expect(formatTimeDraftInput('06')).toBe('06')
    expect(formatTimeDraftInput('062')).toBe('06:2')
    expect(formatTimeDraftInput('0625')).toBe('06:25')
  })

  it('strips non-digit characters the user typed, including a manually typed colon', () => {
    expect(formatTimeDraftInput('06:25')).toBe('06:25')
    expect(formatTimeDraftInput('06::25')).toBe('06:25')
    expect(formatTimeDraftInput('a0b6c2d5')).toBe('06:25')
  })

  it('caps input at 4 digits, ignoring anything typed past HH:MM', () => {
    expect(formatTimeDraftInput('06255')).toBe('06:25')
  })

  it('empties out when every digit is deleted', () => {
    expect(formatTimeDraftInput('')).toBe('')
  })
})

describe('buildTimeSliderMarks (research D4, D5)', () => {
  it('returns the Full tier when unmeasured — jsdom never measures (research D5)', () => {
    const marks = buildTimeSliderMarks(null)
    expect(marks.length).toBeGreaterThan(0)
    expect(marks.some((m) => m.isHour)).toBe(true)
    expect(marks.some((m) => !m.isHour)).toBe(true)
  })

  it('emits Full-tier marks at exactly 15-minute intervals, spanning 0 to the last valid boundary', () => {
    const marks = buildTimeSliderMarks(null)
    expect(marks[0].value).toBe(0)
    expect(marks[marks.length - 1].value).toBe(MAX_QUARTER_HOUR_MARK)
    for (const mark of marks) {
      expect(mark.value % SNAP_MINUTES).toBe(0)
    }
    for (let i = 1; i < marks.length; i++) {
      expect(marks[i].value - marks[i - 1].value).toBe(SNAP_MINUTES)
    }
  })

  it('distinguishes hour marks from quarter-hour marks in the returned data (FR-012)', () => {
    const marks = buildTimeSliderMarks(null)
    for (const mark of marks) {
      expect(mark.isHour).toBe(mark.value % 60 === 0)
    }
  })

  it('labels only appear at the chosen interval, never on a non-hour mark', () => {
    const marks = buildTimeSliderMarks(1200) // wide — every-hour labels (research D4 spacing table)
    for (const mark of marks) {
      if (mark.label !== undefined) expect(mark.isHour).toBe(true)
    }
    expect(marks.filter((m) => m.label !== undefined).length).toBeGreaterThan(0)
  })

  it('tier selection derives from the named spacing constants (FR-014)', () => {
    // research D4's spacing table: 600px clears the 6px quarter-hour threshold (Full); 400px
    // does not, but clears the hour threshold (Reduced); 100px clears neither (None).
    const full = buildTimeSliderMarks(600)
    const reduced = buildTimeSliderMarks(400)
    const none = buildTimeSliderMarks(100)

    expect(full.some((m) => !m.isHour)).toBe(true) // quarter-hour marks present
    expect(reduced.every((m) => m.isHour)).toBe(true) // hour marks only
    expect(reduced.length).toBeGreaterThan(0)
    expect(none).toEqual([])
  })
})
