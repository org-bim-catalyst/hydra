import { useActiveLocationStore } from '../../store/activeLocationStore'
import { redrawScheduler } from '../scene/RedrawScheduler'
import { sceneAnchor } from '../scene/SceneAnchor'

/**
 * Keeps the viewer's single reference point on the active location.
 *
 * Content drawn in the scene is positioned relative to that point: the sun-path dome sits on it,
 * and the site-boundary ring and building footprints are converted into metres from it. Moving it
 * with the active location is what keeps the analysis on the site Lucy confirmed rather than on
 * wherever the map happened to first open.
 *
 * A store subscription rather than a React effect: it runs synchronously the instant the location
 * changes, before any component effect reacts to that same change — so a capability rebuilding
 * for the new site always converts against the new anchor, never the previous one. Geometry that
 * outlives the change rebuilds by subscribing to `sceneAnchor` itself.
 */
useActiveLocationStore.subscribe((state, previous) => {
  if (state.latitude === null || state.longitude === null) return
  if (state.latitude === previous.latitude && state.longitude === previous.longitude) return

  sceneAnchor.set({ latitude: state.latitude, longitude: state.longitude })
  // The camera matrix is rebuilt from the anchor on every draw, but nothing else requests one.
  redrawScheduler.invalidate()
})
