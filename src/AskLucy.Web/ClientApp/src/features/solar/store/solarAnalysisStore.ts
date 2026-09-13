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

const DEFAULT_PLAYBACK_MINUTES_PER_SECOND = 120

function buildMomentFromInstant(instantUtc: Date, timeZoneId: string | null): AnalysisMoment {
  const { localDate, localMinuteOfDay } = toLocalParts(instantUtc, timeZoneId ?? FALLBACK_TIME_ZONE)
  return { instantUtc, localDate, localMinuteOfDay, isPlaying: false, playbackMinutesPerSecond: DEFAULT_PLAYBACK_MINUTES_PER_SECOND }
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
    const moment = previous
      ? buildMomentFromInstant(previous.instantUtc, basis.timeZoneId)
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
    const nextInstant = new Date(
      moment.instantUtc.getTime() + deltaSeconds * moment.playbackMinutesPerSecond * 60_000,
    )
    const { localDate, localMinuteOfDay } = toLocalParts(nextInstant, site?.timeZoneId ?? FALLBACK_TIME_ZONE)
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
