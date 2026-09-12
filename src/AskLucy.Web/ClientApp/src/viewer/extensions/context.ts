import type { ComponentType } from 'react'
import { viewerEngine } from '../engine/viewerEngineInstance'
import type { IViewerEngine } from '../api/engine'
import type { ViewerEventHandler, ViewerEventType } from '../api/events'
import { panelTypeRegistry } from '../panels/registry'
import { useFloatingPanelStore } from '../panels/store/floatingPanelStore'
import type { PanelRequest, PanelTypeDefinition } from '../panels/types/panel'
import { drawingSpaceRegistry, type DrawingSpaceHandle } from '../scene/DrawingSpaceRegistry'
import { useViewerExtensionStore } from './store/viewerExtensionStore'
import type { ToolbarEntry } from './ViewerExtension'

/** contracts/extension-context.md — what a starting extension receives. Every helper closes over
 * `extensionId` so an extension never passes its own id to anything (contract: "every call
 * already knows who made it"), and every helper except `openPanel` records what it did against
 * that id so stop() can withdraw it without the author's cooperation (FR-014, FR-015). */
export interface ExtensionContext {
  readonly engine: IViewerEngine
  on<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): void
  contributeOverlay(component: ComponentType): void
  contributeToolbarEntry(entry: ToolbarEntry): void
  registerLivePanelKind(definition: PanelTypeDefinition): void
  openPanel(request: PanelRequest): void
  /** contracts/extension-context-extensions.md (specs/051) — acquires this extension's own
   * isolated Drawing Space. Idempotent per extension (mirrors specs/050's start-when-started
   * posture). Automatically released on stop — never call `release()` yourself; there isn't one
   * exposed here. */
  acquireDrawingSpace(): DrawingSpaceHandle
}

/** contracts/extension-context.md — a handler that throws is contained and surfaced (FR-017):
 * caught here, recorded via `recordEventFailure` (constitution §2.VIII — never logged-only), and
 * never allowed to stop delivery to the viewer event bus's other subscribers. */
function containEventHandler<E extends ViewerEventType>(
  extensionId: string,
  handler: ViewerEventHandler<E>,
): ViewerEventHandler<E> {
  return (event) => {
    try {
      handler(event)
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      useViewerExtensionStore.getState().recordEventFailure(extensionId, message)
    }
  }
}

/** contracts/extension-context.md — creates the per-extension context passed to `start()`. */
export function createExtensionContext(extensionId: string): ExtensionContext {
  const { addContribution } = useViewerExtensionStore.getState()

  return {
    engine: viewerEngine,

    on(type, handler) {
      const unsubscribe = viewerEngine.on(type, containEventHandler(extensionId, handler))
      addContribution({ kind: 'eventSubscription', extensionId, unsubscribe })
    },

    contributeOverlay(component) {
      addContribution({ kind: 'overlay', extensionId, component })
    },

    contributeToolbarEntry(entry) {
      addContribution({ kind: 'toolbarEntry', extensionId, entry })
    },

    registerLivePanelKind(definition) {
      panelTypeRegistry.register(definition)
      addContribution({ kind: 'livePanelKind', extensionId, typeKey: definition.typeKey })
    },

    openPanel(request) {
      // Deliberately untracked (contract: "a panel the user can close is theirs, not the
      // extension's") — not recorded as a contribution and not withdrawn on stop.
      useFloatingPanelStore.getState().openPanel(request)
    },

    acquireDrawingSpace() {
      const handle = drawingSpaceRegistry.acquire(extensionId)
      // acquireDrawingSpace() is itself idempotent (drawingSpaceRegistry.acquire returns the
      // same handle on a second call), so only record the contribution once — otherwise calling
      // this twice would queue two withdrawal entries for one drawing space.
      const alreadyContributed = useViewerExtensionStore
        .getState()
        .contributions.some((c) => c.kind === 'drawingSpace' && c.extensionId === extensionId)
      if (!alreadyContributed) {
        addContribution({ kind: 'drawingSpace', extensionId, release: () => drawingSpaceRegistry.release(extensionId) })
      }
      return handle
    },
  }
}
