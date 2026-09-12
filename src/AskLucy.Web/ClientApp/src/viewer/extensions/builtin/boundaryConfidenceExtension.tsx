import { SiteBoundaryConfidenceBadge } from '../../../features/viewer/components/SiteBoundaryConfidenceBadge'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** research D8 — contributes the existing `SiteBoundaryConfidenceBadge` unchanged; it renders
 * nothing while no boundary is active, and is the only one of the four migrated capabilities that
 * renders visible DOM of its own. */
export const boundaryConfidenceExtension: ViewerExtension = {
  id: 'viewer.boundary-confidence',
  manifest: { displayName: 'Boundary Confidence', description: 'Shows the confidence level of the resolved site boundary.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(SiteBoundaryConfidenceBadge)
  },
  stop() {},
}

viewerExtensionRegistry.register(boundaryConfidenceExtension)
