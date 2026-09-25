import { create } from 'zustand'
import type { SiteBuildingDto } from '../api/siteBuildingsApi'
import { copy } from '../copy'
import { FALLBACK_TIME_ZONE, fromLocalParts, resolveTimeZone, toLocalParts } from '../solar/timeZone'

/** data-model.md "Site". */
export interface Site {
  latitude: number
  longitude: number
  timeZoneId: string | null
  timeBasisLabel: string
  siteKey: string
}

/** data-model.md "Analysis Moment". */
export interface AnalysisMoment {
  instantUtc: Date
  localDate: string
  localMinuteOfDay: number
  isPlaying: boolean
  playbackMinutesPerSecond: number
}

export type AnalysisStatus = 'idle' | 'loading' | 'ready' | 'partial' | 'failed'

export function siteKeyFor(latitude: number, longitude: number): string {
  return `${latitude.toFixed(6)},${longitude.toFixed(6)}`
}

/** contracts/solar-panels.md "Speed" — the default the 052 contract names. */
export const DEFAULT_PLAYBACK_MINUTES_PER_SECOND = 120

/** The speeds the Time of Day panel offers, in local minutes per real second. The slowest plays a
 * whole day in just under five minutes, slow enough to watch a sunrise edge move; the fastest is
 * the contract default, a whole day in twelve seconds. */
export const PLAYBACK_SPEED_OPTIONS_MINUTES_PER_SECOND = [5, 15, 30, 60, 120] as const

function buildMomentFromInstant(
  instantUtc: Date,
  timeZoneId: string | null,
  playbackMinutesPerSecond: number = DEFAULT_PLAYBACK_MINUTES_PER_SECOND,
): AnalysisMoment {
  const { localDate, localMinuteOfDay } = toLocalParts(instantUtc, timeZoneId ?? FALLBACK_TIME_ZONE)
  return { instantUtc, localDate, localMinuteOfDay, isPlaying: false, playbackMinutesPerSecond }
}

/** The local date after `localDate` (`YYYY-MM-DD`), by calendar arithmetic — no time zone involved. */
function nextLocalDate(localDate: string): string {
  const [year, month, day] = localDate.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10)
}

interface SolarAnalysisState {
  site: Site | null
  moment: AnalysisMoment | null
  status: AnalysisStatus
  failureReason: string | null
  buildingsNotice: string | null
  /** T042, T055 — the current site's building footprints, shared between the overlay (which
   * fetches and rebuilds scene geometry from it) and the corrections panel (which reads the site
   * building's height/provenance from it, FR-011). One source, so the two can never disagree. */
  siteBuildings: SiteBuildingDto[]
  siteBuildingsRadiusMetres: number
  setSiteBuildings: (siteBuildings: SiteBuildingDto[], radiusMetres: number) => void

  /** data-model.md "Analysis State" lifecycle: idle -> loading -> ready|partial|failed. Resolves
   * the site's time zone (research D2) and seeds the analysis moment at "now" for that site. */
  open: (latitude: number, longitude: number) => Promise<void>
  /** research D10 — follow a new site rather than close, recomputing time zone and moment. */
  followSite: (latitude: number, longitude: number) => Promise<void>
  markReady: () => void
  markPartial: (buildingsNotice: string) => void
  markFailed: (failureReason: string) => void
  close: () => void

  /** data-model.md "Analysis Moment" Rule — `instantUtc` is canonical. This is the one setter
   * playback and any direct-instant caller use; local projections are always derived from it. */
  setInstantUtc: (instantUtc: Date) => void
  /** Recomputes `instantUtc` from the edited local date, never the reverse (FR-004). */
  setLocalDate: (localDate: string) => void
  /** Recomputes `instantUtc` from the edited local minute-of-day, never the reverse (FR-004). */
  setLocalMinuteOfDay: (localMinuteOfDay: number) => void
  setPlaying: (isPlaying: boolean) => void
  setPlaybackMinutesPerSecond: (minutesPerSecond: number) => void
  /** Advances the canonical instant by `deltaSeconds * playbackMinutesPerSecond` real seconds —
   * used only by the guarded `onFrame` callback (research D9) while playing. */
  advanceBy: (deltaSeconds: number) => void
}

export const useSolarAnalysisStore = create<SolarAnalysisState>()((set, get) => ({
  site: null,
  moment: null,
  status: 'idle',
  failureReason: null,
  buildingsNotice: null,
  siteBuildings: [],
  siteBuildingsRadiusMetres: 200,
  setSiteBuildings: (siteBuildings, radiusMetres) => set({ siteBuildings, siteBuildingsRadiusMetres: radiusMetres }),

  open: async (latitude, longitude) => {
    set({ status: 'loading', failureReason: null, buildingsNotice: null })
    const basis = await resolveTimeZone(latitude, longitude)
    const site: Site = { latitude, longitude, ...basis, siteKey: siteKeyFor(latitude, longitude) }
    const moment = buildMomentFromInstant(new Date(), basis.timeZoneId)
    set({ site, moment })
  },

  followSite: async (latitude, longitude) => {
    set({ status: 'loading', buildingsNotice: null })
    const basis = await resolveTimeZone(latitude, longitude)
    const site: Site = { latitude, longitude, ...basis, siteKey: siteKeyFor(latitude, longitude) }
    const previous = get().moment
    // The chosen playback speed is a preference, not a time value — following a site to a new
    // place keeps it rather than snapping back to the default.
    const moment = previous
      ? buildMomentFromInstant(previous.instantUtc, basis.timeZoneId, previous.playbackMinutesPerSecond)
      : buildMomentFromInstant(new Date(), basis.timeZoneId)
    set({ site, moment })
  },

  markReady: () => set({ status: 'ready', failureReason: null }),
  markPartial: (buildingsNotice) => set({ status: 'partial', failureReason: null, buildingsNotice }),
  markFailed: (failureReason) => set({ status: 'failed', failureReason }),

  close: () =>
    set({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null, siteBuildings: [], siteBuildingsRadiusMetres: 200 }),

  setInstantUtc: (instantUtc) => {
    const { site, moment } = get()
    if (!moment) return
    const { localDate, localMinuteOfDay } = toLocalParts(instantUtc, site?.timeZoneId ?? FALLBACK_TIME_ZONE)
    set({ moment: { ...moment, instantUtc, localDate, localMinuteOfDay } })
  },

  setLocalDate: (localDate) => {
    const { site, moment } = get()
    if (!moment) return
    const instantUtc = fromLocalParts(localDate, moment.localMinuteOfDay, site?.timeZoneId ?? FALLBACK_TIME_ZONE)
    set({ moment: { ...moment, instantUtc, localDate } })
  },

  setLocalMinuteOfDay: (localMinuteOfDay) => {
    const { site, moment } = get()
    if (!moment) return
    const instantUtc = fromLocalParts(moment.localDate, localMinuteOfDay, site?.timeZoneId ?? FALLBACK_TIME_ZONE)
    set({ moment: { ...moment, instantUtc, localMinuteOfDay } })
  },

  setPlaying: (isPlaying) => {
    const { moment } = get()
    if (!moment) return
    set({ moment: { ...moment, isPlaying } })
  },

  setPlaybackMinutesPerSecond: (playbackMinutesPerSecond) => {
    const { moment } = get()
    if (!moment) return
    set({ moment: { ...moment, playbackMinutesPerSecond } })
  },

  advanceBy: (deltaSeconds) => {
    const { site, moment } = get()
    if (!moment) return
    const timeZoneId = site?.timeZoneId ?? FALLBACK_TIME_ZONE
    let nextInstant = new Date(
      moment.instantUtc.getTime() + deltaSeconds * moment.playbackMinutesPerSecond * 60_000,
    )
    // Each frame moves by a whole frame's worth of minutes (about two at the default speed), so a
    // frame that crosses midnight used to land a minute or more into the new day and every day
    // after the first began at 00:01 or later, never at 00:00. Landing that one frame exactly on
    // local midnight shows each new day from its start; it costs at most one frame of time. The
    // bounds check skips the clamp when local midnight does not exist on that date (a DST change
    // at 00:00, as in Egypt's) and `fromLocalParts` resolves it outside this frame's interval.
    const midnight = fromLocalParts(nextLocalDate(moment.localDate), 0, timeZoneId)
    if (midnight > moment.instantUtc && midnight < nextInstant) {
      nextInstant = midnight
    }
    const { localDate, localMinuteOfDay } = toLocalParts(nextInstant, timeZoneId)
    set({ moment: { ...moment, instantUtc: nextInstant, localDate, localMinuteOfDay } })
  },
}))

/** FR-045/SC-008 helper — every transition into `failed`/`partial` must carry a user-facing
 * string drawn from `copy.ts`, never an empty or developer-only message. Exported so callers
 * (and `solarAnalysisStore.test.ts`) have one place that maps a known failure to its wording. */
export const solarFailureCopy = {
  noActiveSite: copy.noActiveSite,
  viewerUnavailable: copy.viewerUnavailable,
  buildingDataUnavailable: copy.buildingDataUnavailable,
  noBuildingsFound: copy.noBuildingsFound,
} as const
