import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'
import { PanelsExtensionOverlay } from './PanelsExtensionOverlay'

export const panelsExtension: ViewerExtension = {
  id: 'viewer.panels',
  manifest: { displayName: 'Panels', description: 'Floating panels Lucy uses to present content over the viewer.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(PanelsExtensionOverlay)
  },
  stop() {},
}

viewerExtensionRegistry.register(panelsExtension)
