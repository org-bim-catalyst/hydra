import { RiLayoutGridLine } from '@remixicon/react'
import { useFloatingPanelStore } from '../../panels/store/floatingPanelStore'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'
import { PanelsExtensionOverlay } from './PanelsExtensionOverlay'

const useHasOpenPanels = () => useFloatingPanelStore((s) => s.panels.length > 0)

export const panelsExtension: ViewerExtension = {
  id: 'viewer.panels',
  manifest: { displayName: 'Panels', description: 'Floating panels Lucy uses to present content over the viewer.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(PanelsExtensionOverlay)

    // specs/054 FR-005e — "Arrange panels", in the viewer toolbar alongside the other one-tap
    // viewer controls. Contributed first (this extension starts first, declared.ts), so it sits
    // above Solar Analysis. Shown only while there is a panel to arrange.
    context.contributeToolbarEntry({
      id: 'viewer.panels-arrange',
      label: 'Arrange panels',
      icon: RiLayoutGridLine,
      onClick: () => useFloatingPanelStore.getState().arrangeHandler?.(),
      useIsShown: useHasOpenPanels,
    })
  },
  stop() {},
}

viewerExtensionRegistry.register(panelsExtension)
