import { useEffect, useSyncExternalStore } from 'react'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { sceneAnchor } from '../../../viewer/scene/SceneAnchor'
import type { ExtensionContext } from '../../../viewer/extensions/context'
import { useViewerExtensionStore } from '../../../viewer/extensions/store/viewerExtensionStore'
import { getSiteBuildings } from '../api/siteBuildingsApi'
import { copy } from '../copy'
import { daySummary } from '../solar/daySummary'
import { solarPosition } from '../solar/solarPosition'
import { buildSolarFiguresContent } from '../panels/solarFiguresContent'
import type { SolarScene } from '../scene/SolarScene'
import { useCorrectionsStore } from '../store/correctionsStore'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

export const EXTENSION_ID = 'viewer.solar-analysis'
export const FIGURES_PANEL_REQUEST_ID = 'solar-analysis-figures'

/** contracts/open-solar-analysis-capability.md — Lucy's optional requested date/time, applied
 * once the analysis opens for the active site. `activate(mode?)` only carries a single optional
 * string, so this small holder is how `useChatStream.ts` hands the two fields across without
 * threading them through the whole activation call chain. Cleared once applied. */
let pendingRequestedMoment: { date: string; timeOfDay: string } | null = null

export function requestSolarAnalysisMoment(date: string, timeOfDay: string): void {
  pendingRequestedMoment = { date, timeOfDay }
  // If the analysis is already open on a site, nothing downstream will re-run to pick this up:
  // `useChatStream` calls `activate()` right after this, which is idempotent, so neither the
  // activation state nor the active location changes and the follow-site effect never fires.
  // Asking Lucy for a specific time while the analysis is already open is an ordinary request, so
  // apply it here rather than letting it sit pending against some later, unrelated site.
  if (useSolarAnalysisStore.getState().site) applyPendingRequestedMoment()
}

/** contracts/open-solar-analysis-capability.md — applies Lucy's requested date/time, if any, and
 * clears it so it can never be re-applied later to a different site. Both setters recompute
 * `instantUtc` through the site's own time zone, exactly as a direct user edit would (FR-004).
 *
 * Deliberately called on BOTH paths below — a new site and an already-open one. Asking Lucy about
 * a specific time while the analysis is already open on the current site is an ordinary request,
 * not a no-op; leaving it unapplied would silently ignore it, and leaving it *pending* would make
 * it fire later against whatever site the user moved to next. */
function applyPendingRequestedMoment(): void {
  if (!pendingRequestedMoment) return
  const { date, timeOfDay } = pendingRequestedMoment
  pendingRequestedMoment = null
  if (date) useSolarAnalysisStore.getState().setLocalDate(date)
  if (timeOfDay) {
    const [hours, minutes] = timeOfDay.split(':').map(Number)
    if (Number.isFinite(hours) && Number.isFinite(minutes)) {
      useSolarAnalysisStore.getState().setLocalMinuteOfDay(hours * 60 + minutes)
    }
  }
}

/** A UTC instant carrying only the local calendar date's year/month/day — used to sample the
 * sun-path arc for "the chosen day" (contracts/solar-extension.md), independent of the
 * moment-of-day the slider is currently at. */
function calendarDateAsUtcMidnight(localDate: string): Date {
  const [year, month, day] = localDate.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day))
}

/**
 * research D10, FR-008, FR-042 — follows the viewer's active site: opens/re-keys the analysis on
 * a site change, closes only when the viewer has no site at all, and rebuilds the sun-path
 * geometry plus refreshes the figures panel whenever the analysis moment or the site changes
 * (FR-005…FR-008, FR-031). Contributed once via `context.contributeOverlay(...)` in
 * `solarAnalysisExtension.tsx`'s `start()` — this component renders nothing itself; it exists to
 * run these effects for as long as the extension is started (the framework renders every
 * contributed overlay unconditionally, regardless of activation — the effects below gate on
 * `activation === 'active'` themselves, contracts/solar-extension.md).
 */
export function makeSolarAnalysisOverlay(context: ExtensionContext, sceneRef: { current: SolarScene | null }) {
  return function SolarAnalysisOverlay() {
    const activation = useViewerExtensionStore((s) => s.extensions[EXTENSION_ID]?.activation)
    const latitude = useActiveLocationStore((s) => s.latitude)
    const longitude = useActiveLocationStore((s) => s.longitude)
    const site = useSolarAnalysisStore((s) => s.site)
    const moment = useSolarAnalysisStore((s) => s.moment)
    const status = useSolarAnalysisStore((s) => s.status)
    const siteBuildings = useSolarAnalysisStore((s) => s.siteBuildings)
    const showBuildingMass = useSolarAnalysisStore((s) => s.showBuildingMass)
    // specs/076 — the site's resolved outline, so the dome encloses the whole site and not just the
    // one footprint flagged as the site building.
    const siteBoundary = useActiveSiteBoundaryStore((s) => s.polygon)
    // FR-025, FR-026, research D11 — reactive: a correction edit (a different object reference
    // for this site key) re-triggers the geometry-rebuild effect below, without depending on
    // corrections for any OTHER site re-rendering this overlay.
    const corrections = useCorrectionsStore((s) => (site ? s.bySiteKey[site.siteKey] : undefined))

    // Opens the analysis for the active site, or follows a new one — never continues against a
    // site that no longer matches the viewer's own (research D10).
    useEffect(() => {
      if (activation !== 'active') return
      const store = useSolarAnalysisStore.getState()

      if (latitude === null || longitude === null) {
        if (store.site) store.close() // FR-042: no site at all -> close, never continue stale.
        else store.markFailed(copy.noActiveSite)
        return
      }

      const isNewSite = !store.site || store.site.latitude !== latitude || store.site.longitude !== longitude
      if (!isNewSite) {
        // Already open on this site — nothing to re-resolve, but a moment Lucy asked for still
        // has to land, and still has to be cleared (see applyPendingRequestedMoment).
        applyPendingRequestedMoment()
        return
      }

      let cancelled = false
      void store.followSite(latitude, longitude).then(() => {
        if (cancelled) return
        applyPendingRequestedMoment()
        useSolarAnalysisStore.getState().markReady()
      })
      return () => {
        cancelled = true
      }
    }, [activation, latitude, longitude])

    // T042, FR-009, FR-013, FR-014, FR-015, research D10 — fetches buildings on activation and on
    // every site change, tagging the request with the site's own key and discarding any response
    // whose key no longer matches by the time it resolves, so the display never mixes one site's
    // buildings with another's sun (the stale-data guard the edge case in spec.md calls for). The
    // `cancelled` flag additionally guards against this exact overlay instance being torn down
    // (e.g. the extension deactivating) before its own in-flight request resolves — the site-key
    // check alone cannot catch that case when a user returns to the same site in a new mount.
    // Geometry (T056/T057's correction-aware rebuild) is handled by the separate effect below,
    // keyed on `siteBuildings`/`corrections` rather than performed here — this effect's only job
    // is the network call and the notice/status it implies.
    useEffect(() => {
      if (activation !== 'active' || !site) return
      const requestedSiteKey = site.siteKey
      let cancelled = false

      getSiteBuildings(site.latitude, site.longitude)
        .then((response) => {
          if (cancelled || useSolarAnalysisStore.getState().site?.siteKey !== requestedSiteKey) return // stale

          useSolarAnalysisStore.getState().setSiteBuildings(response.buildings, response.radiusMetres)

          // FR-013, FR-014, FR-015 — every "less than we wanted" case gets a stated notice; the
          // sun path keeps working regardless (partial is not a failure state).
          if (response.buildings.length === 0) {
            useSolarAnalysisStore.getState().markPartial(copy.noBuildingsFound)
          } else if (response.limited) {
            useSolarAnalysisStore.getState().markPartial(copy.buildingDataLimited(response.buildings.length))
          } else if (response.excludedCount > 0) {
            useSolarAnalysisStore.getState().markPartial(copy.buildingsExcluded(response.excludedCount))
          } else {
            useSolarAnalysisStore.getState().markReady()
          }
        })
        .catch(() => {
          // FR-014, FR-045, constitution §2.VIII — never swallowed: the sun path keeps working,
          // and the user is told buildings could not be retrieved, never left with nothing to read.
          if (cancelled || useSolarAnalysisStore.getState().site?.siteKey !== requestedSiteKey) return // stale
          useSolarAnalysisStore.getState().setSiteBuildings([], 200)
          useSolarAnalysisStore.getState().markPartial(copy.buildingDataUnavailable)
        })

      return () => {
        cancelled = true
      }
    }, [activation, site])

    // Footprints are converted into metres from the scene's reference point when built. That
    // point follows the active location, so a move must rebuild them — otherwise the buildings of
    // the site being left would render shifted onto the new one until its own data arrived.
    const anchorVersion = useSyncExternalStore(sceneAnchor.subscribe, () => sceneAnchor.version)

    // T040, T056, T057, FR-010, FR-019, FR-025, FR-026, research D13, D15 — rebuilds the
    // buildings group (with any height corrections applied) and the ground offset whenever the
    // fetched buildings OR this site's corrections change, or the reference point moves.
    // Deliberately NOT keyed on `moment` — this must never run on a time-of-day tick (that would
    // defeat T051's whole point).
    useEffect(() => {
      const solarScene = sceneRef.current
      if (activation !== 'active' || !solarScene) return

      const correctedBuildings = siteBuildings.map((b) =>
        corrections?.buildingHeights[b.id] !== undefined ? { ...b, heightMetres: corrections.buildingHeights[b.id] } : b,
      )

      try {
        // T023/T033 — the shadow rig is sized from the footprints themselves, measured as they are
        // built, not from the radius they were queried with. `rebuildBuildings` reports those
        // bounds to the scene, so there is nothing to compute here.
        const built = solarScene.rebuildBuildings(correctedBuildings, siteBoundary)
        solarScene.setGroundOffset(corrections?.groundOffsetMetres ?? 0)
        solarScene.invalidate()

        // T032, FR-028 — a footprint whose ring is too degenerate to extrude is visible on the
        // basemap but casts nothing, so it is counted and stated rather than quietly missing. The
        // fetch effect already reports the server's own exclusions; this covers the ones only the
        // geometry pass can find.
        if (built.excludedCount > 0) {
          useSolarAnalysisStore.getState().markPartial(copy.buildingsExcluded(built.excludedCount))
        }
      } catch {
        // Geometry construction (worldToLocal, extrusion) is deliberately isolated from the
        // network call's own failure branch above — an exception building geometry from a
        // perfectly valid response must never be mislabeled as "building data unavailable"
        // (constitution §2.VIII: the failure surfaced must describe what actually failed).
        useSolarAnalysisStore.getState().markFailed(copy.viewerUnavailable)
      }
    }, [activation, siteBuildings, corrections, siteBoundary, anchorVersion])

    // FR-016 — the massing switch flips the one shared material in place; no rebuild. Keyed on
    // `activation` too, because a re-activated extension builds a fresh scene that starts hidden.
    useEffect(() => {
      const solarScene = sceneRef.current
      if (activation !== 'active' || !solarScene) return
      solarScene.setShowBuildingMass(showBuildingMass)
      solarScene.invalidate()
    }, [activation, showBuildingMass])

    // Rebuilds the sun-path geometry, aims the shadow light, and refreshes the figures panel
    // whenever the instant or the site changes (FR-005…FR-008, FR-016, FR-017, FR-031).
    useEffect(() => {
      const solarScene = sceneRef.current
      if (activation !== 'active' || !site || !moment || !solarScene || status === 'failed') return

      const dateForArc = calendarDateAsUtcMidnight(moment.localDate)
      const position = solarPosition(moment.instantUtc, site.latitude, site.longitude)

      // T051, FR-019, FR-022, FR-023, SC-004 — rebuilds the dome only on a date/site change;
      // every other tick just moves the marker and the light, never rebuilding tube geometry.
      const domeRebuilt = solarScene.updateSunPath(moment.localDate, site.latitude, site.longitude, moment.instantUtc, position.azimuthDegrees, position.altitudeDegrees)

      // T035, T036, FR-018, FR-020, FR-024 — during continuous playback only, a sun movement too
      // small to change the picture is skipped: `aimSun` returns false and no `invalidate()` is
      // issued, so the framework's on-demand renderer never runs a frame — and never runs the
      // shadow-map pass — for that tick. Withholding the redraw is the entire mechanism; nothing
      // here touches renderer-global state (FR-024). Scrubbing and single-step changes pass
      // `isPlaying: false` and are therefore never gated.
      const sunMoved = solarScene.aimSun(position.azimuthDegrees, position.altitudeDegrees, moment.isPlaying)
      if (domeRebuilt || sunMoved) solarScene.invalidate()

      // The figures below are rebuilt unconditionally — US3 requires the readout to keep pace with
      // playback even on a tick whose frame was skipped, and it costs no drawing.

      const summary = daySummary(dateForArc, site.latitude, site.longitude)
      const siteBuilding = siteBuildings.find((b) => b.isSiteBuilding)
      const content = buildSolarFiguresContent({
        localDate: moment.localDate,
        localMinuteOfDay: moment.localMinuteOfDay,
        timeZoneId: site.timeZoneId,
        timeBasisLabel: site.timeBasisLabel,
        solarPosition: position,
        daySummary: summary,
        siteBuildingHeightAssumed: siteBuilding ? siteBuilding.heightProvenance === 'assumed' : null,
      })

      context.openPanel({
        kind: 'content',
        requestId: FIGURES_PANEL_REQUEST_ID,
        title: copy.toolbarLabel,
        content,
        // The reference page's compact look: this readout sits over the scene the analysis is
        // about, so it keeps its footprint small.
        chrome: { density: 'compact', defaultSize: { width: 260, height: 320 } },
      })
    }, [activation, site, moment, status, siteBuildings])

    return null
  }
}
