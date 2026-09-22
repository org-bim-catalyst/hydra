/**
 * specs/064-precise-time-control — the pure decision logic behind the Time of Day panel's entry
 * field and slider. Everything here is a function of its arguments only: no DOM, no store, no
 * clock, no timezone database of its own (research D6). MUI Slider pointer-dragging is close to
 * untestable in jsdom, so no decision may live inside a drag handler — it lives here instead,
 * where it is a plain unit test.
 */

/** The day this panel controls is always exactly 1440 minutes (data-model.md "Named constants"). */
export const MINUTES_PER_DAY = 1440

/** The interval requested by the user, and the increment slider dragging settles on (FR-016). */
export const SNAP_MINUTES = 15

/** research D4 — below this, adjacent ticks visually merge into a band rather than reading as
 * discrete marks. Derived from measured track width, never a hard-coded panel-width breakpoint. */
export const MIN_TICK_SPACING_PX = 6

/** research D4 — the rendered label is the full `HH:MM` string (e.g. "18:00"), not just the hour,
 * at the panel's actual 9px monospace label size (`SolarTimeControlPanel.tsx`'s
 * `.MuiSlider-markLabel` sx) — roughly 27px wide centered on its tick, so this is the minimum pitch
 * at which adjacent labels stop touching, with a small margin so they read as separate at a glance. */
export const MIN_LABEL_SPACING_PX = 46

const HOUR_MINUTES = 60

/** The last minute a 15-minute-boundary value can occupy without reaching the next day (23:45).
 * `snapToQuarterHour` and the Full tick tier both stop here — FR-018 forbids ever landing on 24:00. */
const MAX_QUARTER_HOUR_MARK = MINUTES_PER_DAY - SNAP_MINUTES

/** The last minute an hour-boundary value can occupy without reaching the next day (23:00). */
const MAX_HOUR_MARK = MINUTES_PER_DAY - HOUR_MINUTES

/** How many quarter-hour and hour boundaries a day holds — the divisors data-model.md's tick-tier
 * table expresses spacing against (`width / 96`, `width / 24`). */
const QUARTER_TICK_COUNT = MINUTES_PER_DAY / SNAP_MINUTES
const HOUR_TICK_COUNT = MINUTES_PER_DAY / HOUR_MINUTES

/** `HH:MM`, zero-padded. Moved from `SolarTimeControlPanel.tsx`, behaviour unchanged — the exact
 * inverse of `parseLocalTimeEntry` for every valid minute. */
export function formatLocalTime(localMinuteOfDay: number): string {
  const hour = Math.floor(localMinuteOfDay / 60)
  const minute = localMinuteOfDay % 60
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
}

export type TimeEntryResult = { ok: true; minuteOfDay: number } | { ok: false; reason: 'malformed' | 'outOfRange' }

const TIME_ENTRY_PATTERN = /^(\d{1,2}):(\d{2})$/

/**
 * contracts/time-entry.md — text the user typed to a minute of the day, or a named reason for
 * refusal. Never throws, never clamps: `25:00` is a mistake to report (FR-004), not a number to
 * round down. A discriminated union so an unhandled refusal is a compile error, not a value that
 * silently reads as success (§4 TypeScript).
 */
export function parseLocalTimeEntry(text: string): TimeEntryResult {
  const match = TIME_ENTRY_PATTERN.exec(text.trim())
  if (!match) return { ok: false, reason: 'malformed' }

  const hour = Number(match[1])
  const minute = Number(match[2])
  if (hour > 23 || minute > 59) return { ok: false, reason: 'outOfRange' }

  return { ok: true, minuteOfDay: hour * 60 + minute }
}

/** FR-016, FR-018 — nearest multiple of `SNAP_MINUTES`, clamped so a value close to midnight
 * (23:53) snaps down to 23:45 rather than up to a nonexistent 24:00. */
export function snapToQuarterHour(minuteOfDay: number): number {
  const clamped = Math.min(Math.max(minuteOfDay, 0), MINUTES_PER_DAY - 1)
  const snapped = Math.round(clamped / SNAP_MINUTES) * SNAP_MINUTES
  return Math.min(snapped, MAX_QUARTER_HOUR_MARK)
}

export interface SliderMark {
  value: number
  label?: string
  /** Lets the component style hour ticks more prominently than quarter-hour ticks by reading
   * this field directly, without re-deriving `minute % 60 === 0` itself (contracts/time-entry.md). */
  isHour: boolean
}

type TickTier = 'full' | 'reduced' | 'none'

function selectTickTier(trackWidthPx: number | null): TickTier {
  // research D5 — unmeasured is Full, not None. jsdom never measures, and `ResizeObserver` is
  // absent there; degrading to the richest tier (rather than the emptiest) mirrors the precedent
  // in hooks/useWholeRowScroll.ts, and keeps ticks present and assertable under test.
  if (trackWidthPx === null) return 'full'
  if (trackWidthPx / QUARTER_TICK_COUNT >= MIN_TICK_SPACING_PX) return 'full'
  if (trackWidthPx / HOUR_TICK_COUNT >= MIN_TICK_SPACING_PX) return 'reduced'
  return 'none'
}

/** The densest hour interval — every hour, else every 3, else every 6 — whose label pitch clears
 * `MIN_LABEL_SPACING_PX`; `null` when even the sparsest interval would overlap. */
function selectLabelIntervalHours(trackWidthPx: number | null): number | null {
  if (trackWidthPx === null) return 1
  for (const hours of [1, 3, 6]) {
    const labelCount = HOUR_TICK_COUNT / hours
    if (trackWidthPx / labelCount >= MIN_LABEL_SPACING_PX) return hours
  }
  return null
}

/**
 * contracts/time-entry.md — tick marks for the time slider, tiered by measured track width per
 * data-model.md's tick-tier table. Ticks span the full range of values dragging can settle on
 * (0 through the tier's last boundary) — they mark exactly where a drag can land, not a day
 * boundary the slider itself never reaches (FR-011, FR-014).
 */
export function buildTimeSliderMarks(trackWidthPx: number | null): SliderMark[] {
  const tier = selectTickTier(trackWidthPx)
  if (tier === 'none') return []

  const labelIntervalHours = selectLabelIntervalHours(trackWidthPx)
  const shouldLabelHour = (minute: number) =>
    labelIntervalHours !== null && (minute / HOUR_MINUTES) % labelIntervalHours === 0

  if (tier === 'reduced') {
    const marks: SliderMark[] = []
    for (let minute = 0; minute <= MAX_HOUR_MARK; minute += HOUR_MINUTES) {
      marks.push({ value: minute, isHour: true, label: shouldLabelHour(minute) ? formatLocalTime(minute) : undefined })
    }
    return marks
  }

  const marks: SliderMark[] = []
  for (let minute = 0; minute <= MAX_QUARTER_HOUR_MARK; minute += SNAP_MINUTES) {
    const isHour = minute % HOUR_MINUTES === 0
    marks.push({ value: minute, isHour, label: isHour && shouldLabelHour(minute) ? formatLocalTime(minute) : undefined })
  }
  return marks
}

/** Reformats whatever the user just typed into the entry field into `H`, `HH`, `HH:M` or `HH:MM` as
 * digits accumulate, inserting the `:` automatically rather than requiring the user to type it —
 * pure function of the field's raw text, so it composes with `parseLocalTimeEntry` unchanged
 * (FR-003 malformed/out-of-range validation still runs on the formatted result, not this one). */
export function formatTimeDraftInput(rawInput: string): string {
  const digits = rawInput.replace(/\D/g, '').slice(0, 4)
  if (digits.length <= 2) return digits
  return `${digits.slice(0, 2)}:${digits.slice(2)}`
}
