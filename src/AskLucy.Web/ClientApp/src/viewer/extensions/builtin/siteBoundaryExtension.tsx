import { SiteBoundaryOverlay } from '../../../features/viewer/components/SiteBoundaryOverlay'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** research D8/FR-033 — migrated last: this capability carries the most post-release history
 * (specs/042's bug-fix rounds, the specs/044 regression). Contributes `SiteBoundaryOverlay`
 * unchanged — its `handle.setSiteBoundary(null)` cleanup path is exactly what specs/044 touched,
 * so it is left untouched here too; `stop()` needs nothing beyond withdrawing the contribution. */
export const siteBoundaryExtension: ViewerExtension = {
  id: 'viewer.site-boundary',
  manifest: { displayName: 'Site Boundary', description: 'Highlights the currently resolved site boundary in the map.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(SiteBoundaryOverlay)
  },
  stop() {},
}

viewerExtensionRegistry.register(siteBoundaryExtension)
