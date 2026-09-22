import { CONTENT_VOCABULARY_VERSION, type PanelContent } from '../../../viewer/panels/content/blocks'
import { copy } from '../copy'
import type { DaySummary } from '../solar/daySummary'
import { SOLAR_SEMIDIAMETER_DEGREES } from '../solar/refraction'
import { SOLAR_POSITION_TOLERANCE_DEGREES, type SolarPositionResult } from '../solar/solarPosition'

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

function formatLocalDateHeading(localDate: string): string {
  const [year, month, day] = localDate.split('-').map(Number)
  return `${day} ${MONTH_NAMES[month - 1]} ${year}`
}

function formatLocalTime(localMinuteOfDay: number): string {
  const hour = Math.floor(localMinuteOfDay / 60)
  const minute = localMinuteOfDay % 60
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
}

function formatUtcAsLocalTime(instantUtc: Date, timeZoneId: string): string {
  return new Intl.DateTimeFormat('en-GB', { timeZone: timeZoneId, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(
    instantUtc,
  )
}

/**
 * T017 — rise and set are solved to the millisecond but shown to the minute, and the user's
 * verification workflow types the shown time straight back into the time field (quickstart Check 1,
 * specs/064). So the shown minute has to be a minute on which the claim it carries is true:
 * sunrise rounds **up** and sunset rounds **down**, making the printed pair the whole minutes
 * during which the sun is up.
 *
 * Rounding to nearest, or truncating, would put the printed sunrise up to a minute *before* the
 * sun actually rises — and at the equator the sun climbs 0.25° in that minute, more than twice the
 * whole position tolerance. Retyping it would then correctly report the sun as still down, and the
 * panel would be back to contradicting itself in exactly the way baseline.md records. The cost is
 * that the printed time can sit up to 59 s after the true crossing; that is inside
 * `RISE_SET_TOLERANCE_SECONDS`, and it errs toward a statement that is true rather than one that
 * is not.
 */
function formatUtcAsLocalMinuteAtOrAfter(instantUtc: Date, timeZoneId: string): string {
  return formatUtcAsLocalTime(new Date(Math.ceil(instantUtc.getTime() / 60_000) * 60_000), timeZoneId)
}

function formatUtcAsLocalMinuteAtOrBefore(instantUtc: Date, timeZoneId: string): string {
  return formatUtcAsLocalTime(new Date(Math.floor(instantUtc.getTime() / 60_000) * 60_000), timeZoneId)
}

function formatDayLength(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const remainingMinutes = Math.round(minutes % 60)
  return `${hours} h ${remainingMinutes} min`
}

export interface SolarFiguresInput {
  localDate: string
  localMinuteOfDay: number
  timeZoneId: string | null
  timeBasisLabel: string
  solarPosition: SolarPositionResult
  daySummary: DaySummary
  /** `null` when no building has been identified as the site's own (FR-012 "returning none is a
   * legitimate outcome"). `true`/`false` otherwise — drives whether the assumed-heights statement
   * in the closing text block is truly load-bearing for what's on screen right now. The statement
   * itself is always present regardless (contracts/solar-panels.md), since a user may still be
   * looking at other buildings whose heights were assumed. */
  siteBuildingHeightAssumed: boolean | null
}

/**
 * contracts/solar-panels.md "Solar Figures" — composes the figures as specs/049 content blocks,
 * client-side (research D12). FR-031 requires this be panel content, not a bespoke component;
 * FR-043/FR-044/SC-010 require the closing statement always be present; FR-002/FR-017/FR-028
 * require the polar, below-horizon and solver-failed cases replace/augment the normal rows rather
 * than showing a blank.
 *
 * The accuracy figure is never hand-typed — it comes from `SOLAR_POSITION_TOLERANCE_DEGREES`,
 * the same constant `solarPosition.test.ts` asserts the implementation against (research D1), so
 * what the user is told cannot drift from what is actually verified.
 */
export function buildSolarFiguresContent(input: SolarFiguresInput): PanelContent {
  const { solarPosition, daySummary, localDate, localMinuteOfDay, timeBasisLabel, timeZoneId, siteBuildingHeightAssumed } = input

  const localTime = formatLocalTime(localMinuteOfDay)
  const keyValueItems: { label: string; value: string } [] = []

  if (daySummary.riseSetUndetermined) {
    // FR-028 — the solver failed. Say so, in the same place the times would have been. Leaving the
    // rows out, or showing the unconverged estimate, would both present a failure as an answer.
    keyValueItems.push({ label: copy.sunriseLabel, value: copy.riseSetUndetermined })
  } else if (daySummary.polarCondition === 'polar-night') {
    keyValueItems.push({ label: copy.sunriseLabel, value: copy.sunNeverRises })
  } else if (daySummary.polarCondition === 'midnight-sun') {
    keyValueItems.push({ label: copy.sunsetLabel, value: copy.sunNeverSets })
  } else {
    const effectiveZone = timeZoneId ?? 'UTC'
    keyValueItems.push(
      { label: copy.sunriseLabel, value: formatUtcAsLocalMinuteAtOrAfter(daySummary.sunriseUtc!, effectiveZone) },
      { label: copy.sunsetLabel, value: formatUtcAsLocalMinuteAtOrBefore(daySummary.sunsetUtc!, effectiveZone) },
      { label: copy.dayLengthLabel, value: formatDayLength(daySummary.dayLengthMinutes!) },
    )
  }

  keyValueItems.push({ label: copy.timesShownInLabel, value: timeBasisLabel })

  const closingStatements = [
    copy.designStageStudyStatement,
    copy.altitudeQuantityStatement,
    copy.accuracyStatement(SOLAR_POSITION_TOLERANCE_DEGREES),
    copy.assumedHeightsStatement,
  ]

  // FR-004 / T017 — the horizon is defined once, by the sun's upper edge, and the same definition
  // decides this notice as decides rise and set. Testing the *centre* against zero, as this did,
  // made the panel print "Sunrise 06:41" and "the sun is below the horizon" at 06:41 together
  // (baseline.md): at sunrise the centre is one solar radius down by definition, and always will
  // be, so the old test could never agree with the time printed beside it.
  const upperEdgeAltitudeDegrees = solarPosition.altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES
  if (upperEdgeAltitudeDegrees < 0) {
    closingStatements.unshift(copy.belowHorizonNotice)
  }

  const blocks: PanelContent['blocks'] = [
    { kind: 'heading', text: copy.figuresPanelTitleFor(formatLocalDateHeading(localDate), localTime), level: 1 },
    { kind: 'metric', label: copy.azimuthLabel, value: Number(solarPosition.azimuthDegrees.toFixed(1)), unit: '°' },
    { kind: 'metric', label: copy.altitudeLabel, value: Number(solarPosition.altitudeDegrees.toFixed(1)), unit: '°' },
    { kind: 'keyValue', items: keyValueItems },
    { kind: 'divider' },
    { kind: 'text', text: closingStatements.join(' ') },
  ]

  // siteBuildingHeightAssumed is read for callers that want to react to it (e.g. surfacing the
  // corrections panel prominently); it does not change which statements this document carries,
  // since the closing text block's assumed-heights statement is always present regardless.
  void siteBuildingHeightAssumed

  return { version: CONTENT_VOCABULARY_VERSION, blocks }
}
