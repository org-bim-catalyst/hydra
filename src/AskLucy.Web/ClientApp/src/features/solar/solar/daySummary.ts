import { solarPosition } from './solarPosition'

/** data-model.md "Day Summary" — sunrise, sunset and day length for a site and date, including
 * the cases where the sun does not rise or does not set (FR-002). */
export interface DaySummary {
  sunriseUtc: Date | null
  sunsetUtc: Date | null
  dayLengthMinutes: number | null
  polarCondition: 'none' | 'midnight-sun' | 'polar-night'
}

const toRad = (degrees: number): number => (degrees * Math.PI) / 180
const toDeg = (radians: number): number => (radians * 180) / Math.PI

/**
 * data-model.md "Day Summary" — computed at solar noon (12:00 UTC on `dateUtc`'s calendar date)
 * for the declination and equation-of-time NOAA needs, per the reference implementation's proven
 * approach. `polarCondition` is derived from the same `cosH0` bound the reference implementation
 * uses to distinguish "never rises" from "never sets" (FR-002) — the ported implementation's
 * `cosH0 > 1` (sun never rises above the −0.833° reference altitude — polar night) and
 * `cosH0 < -1` (sun never dips below it — midnight sun) already carry that distinction, so a
 * single sign check is sufficient and two null returns are never conflated.
 *
 * @param dateUtc Any instant on the calendar date (UTC) to summarize — only its UTC
 * year/month/day are read. Callers pass the site's local date translated to a UTC instant via
 * `timeZone.ts`, so a date near a time-zone boundary summarizes the day the site actually means.
 */
export function daySummary(dateUtc: Date, latitude: number, longitude: number): DaySummary {
  const solarNoonReference = new Date(
    Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 12, 0, 0),
  )
  const { declination, eqTime } = solarPosition(solarNoonReference, latitude, longitude)

  const latRad = toRad(latitude)
  const declRad = toRad(declination)
  const cosH0 =
    (Math.sin(toRad(-0.833)) - Math.sin(latRad) * Math.sin(declRad)) / (Math.cos(latRad) * Math.cos(declRad))

  if (cosH0 > 1) {
    // The sun never rises above the horizon on this date at this latitude.
    return { sunriseUtc: null, sunsetUtc: null, dayLengthMinutes: null, polarCondition: 'polar-night' }
  }
  if (cosH0 < -1) {
    // The sun never sets on this date at this latitude.
    return { sunriseUtc: null, sunsetUtc: null, dayLengthMinutes: null, polarCondition: 'midnight-sun' }
  }

  const H0 = toDeg(Math.acos(cosH0))
  const solarNoonMinutes = 720 - 4 * longitude - eqTime
  const riseMinutes = solarNoonMinutes - H0 * 4
  const setMinutes = solarNoonMinutes + H0 * 4

  const dayStartUtc = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 0, 0, 0)
  const sunriseUtc = new Date(dayStartUtc + riseMinutes * 60_000)
  const sunsetUtc = new Date(dayStartUtc + setMinutes * 60_000)

  return {
    sunriseUtc,
    sunsetUtc,
    dayLengthMinutes: setMinutes - riseMinutes,
    polarCondition: 'none',
  }
}
