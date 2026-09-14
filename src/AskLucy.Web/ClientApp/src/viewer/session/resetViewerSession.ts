import { useAuthStore } from '../../store/authStore'
import { viewerEngine } from '../engine/viewerEngineInstance'
import { DECLARED_EXTENSIONS } from '../extensions/declared'
import { viewerExtensionLoader } from '../extensions/loader'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { viewerSession } from './viewerSession'

/**
 * Ends the viewer session: stops every declared extension (withdrawing its contributions, drawing
 * spaces and activation), unloads the map content, and forgets the camera.
 *
 * Extensions deliberately keep running while the user moves between routes, so open panels, an
 * active analysis and the camera survive a trip to another page. That makes sign-out the one
 * moment the session must end — otherwise the next person to sign in on this tab would inherit
 * the previous user's analysis, site and contributions. `loader.stop()` contains every failure
 * itself, so these calls cannot reject.
 */
export function resetViewerSession(): void {
  for (const id of DECLARED_EXTENSIONS) {
    void viewerExtensionLoader.stop(id)
  }
  if (viewerSession.mapContentId) {
    viewerEngine.unloadContent(viewerSession.mapContentId)
    viewerSession.mapContentId = null
  }
  viewerSession.camera = null
  useViewerEngineStore.getState().setContentMode('placeholder')
}

// A signed-in session becoming signed-out. Rehydrating a persisted session goes null -> id, which
// this correctly ignores.
useAuthStore.subscribe((state, previous) => {
  if (previous.userId !== null && state.userId === null) {
    resetViewerSession()
  }
})
