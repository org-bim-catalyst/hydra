import { SiteBoundaryConfidenceBadge } from '../../../features/viewer/components/SiteBoundaryConfidenceBadge'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** research D8 — contributes the existing `SiteBoundaryConfidenceBadge`; it renders nothing while
 * no boundary is active, and is the only one of the four migrated capabilities that renders visible
 * DOM of its own. specs/073: contributed as a `hudItem`, so it sits in the studio's top-left HUD
 * row after the weather card instead of positioning itself as a free overlay. */
export const boundaryConfidenceExtension: ViewerExtension = {
  id: 'viewer.boundary-confidence',
  manifest: { displayName: 'Boundary Confidence', description: 'Shows the confidence level of the resolved site boundary.' },
  start(context: ExtensionContext) {
    context.contributeHudItem(SiteBoundaryConfidenceBadge)
  },
  stop() {},
}

viewerExtensionRegistry.register(boundaryConfidenceExtension)
