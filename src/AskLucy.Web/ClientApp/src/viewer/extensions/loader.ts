import { createExtensionContext } from './context'
import { panelTypeRegistry } from '../panels/registry'
import { useFloatingPanelStore } from '../panels/store/floatingPanelStore'
import { viewerExtensionRegistry } from './registry'
import { useViewerExtensionStore } from './store/viewerExtensionStore'

/** research D4: "a few seconds — long enough that no correct extension ever trips it on a slow
 * device, short enough that a hung one is reported while the user is still looking at the
 * screen." None of the four migrated capabilities does async work in `start()`, so nothing in
 * this feature exercises this path; it exists for specs/052. */
const START_TIMEOUT_MS = 5000

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

/** T028/T052 — undoes every contribution an extension made: unregisters any live panel kind from
 * specs/049's `panelTypeRegistry` (FR-036 depends on this actually running before a panel of that
 * kind is left rendering against a capability that is gone), and — the leak the reference
 * implementation's own samples get wrong (research D7) — actually calls each event subscription's
 * `unsubscribe()`. Removing the contribution record alone does nothing to the underlying viewer
 * event bus listener; SC-004's fifty-cycle test is what catches a regression here. */
function withdrawContributions(id: string): void {
  const store = useViewerExtensionStore.getState()
  for (const contribution of store.contributions) {
    if (contribution.extensionId !== id) continue
    if (contribution.kind === 'livePanelKind') {
      panelTypeRegistry.unregister(contribution.typeKey)
      useFloatingPanelStore.getState().markLivePanelKindUnavailable(contribution.typeKey)
    } else if (contribution.kind === 'eventSubscription') {
      contribution.unsubscribe()
    }
  }
  store.removeContributionsFor(id)
}

/** data-model.md "Validation Summary" / research D4, D9 — starts and stops declared extensions by
 * id, containing every failure so one extension's problem never blocks another's (FR-009, FR-011,
 * FR-012, FR-029, FR-030), and enforcing the idempotency and race rules a double-invoked React 19
 * Strict Mode effect actually exercises (FR-008, FR-010). */
class ViewerExtensionLoader {
  /** Tracks an in-flight start's own generation so a stop that races it can tell "this is still
   * the start I need to cancel the contributions of" from "a later start already superseded it." */
  private readonly startGenerations = new Map<string, number>()

  async start(id: string): Promise<void> {
    const extension = viewerExtensionRegistry.resolve(id)
    const store = useViewerExtensionStore.getState()

    if (!extension) {
      // FR-009: a declared id that was never registered is surfaced, not thrown — it must not
      // prevent the remaining declared extensions from starting.
      store.setLifecycle(id, 'failed', `Extension "${id}" is not registered.`)
      return
    }

    const currentLifecycle = store.extensions[id]?.lifecycle
    if (currentLifecycle === 'starting' || currentLifecycle === 'started') {
      // FR-008: start-when-started (or already starting) is a no-op — no duplicate contributions.
      return
    }

    const generation = (this.startGenerations.get(id) ?? 0) + 1
    this.startGenerations.set(id, generation)

    store.setLifecycle(id, 'starting')
    const context = createExtensionContext(id)

    try {
      await this.withTimeout(extension.start(context), START_TIMEOUT_MS)
    } catch (error) {
      // Covers both a thrown/rejected start and a start that exceeded its timeout — research D4
      // treats the latter exactly as the former.
      withdrawContributions(id)
      useViewerExtensionStore.getState().setLifecycle(id, 'failed', messageOf(error))
      return
    }

    if (this.startGenerations.get(id) !== generation) {
      // FR-010: a stop raced this start and already won — discard whatever this in-flight start
      // contributed and leave the lifecycle state the stop already set.
      withdrawContributions(id)
      return
    }

    useViewerExtensionStore.getState().setLifecycle(id, 'started', null)
    if (extension.manifest.toggleable) {
      useViewerExtensionStore.getState().setActivation(id, 'inactive')
    }
  }

  async stop(id: string): Promise<void> {
    const extension = viewerExtensionRegistry.resolve(id)
    const store = useViewerExtensionStore.getState()
    const currentLifecycle = store.extensions[id]?.lifecycle

    if (!extension || currentLifecycle === undefined || currentLifecycle === 'not-started' || currentLifecycle === 'stopped') {
      // FR-008: stop-when-not-started is a no-op.
      return
    }

    if (currentLifecycle === 'starting') {
      // FR-010: bump the generation so the in-flight start (above) knows it lost the race and
      // discards its own contributions when it resolves.
      this.startGenerations.set(id, (this.startGenerations.get(id) ?? 0) + 1)
      useViewerExtensionStore.getState().setLifecycle(id, 'stopped')
      return
    }

    try {
      await extension.stop()
    } catch (error) {
      // FR-030: surfaced and recorded; must not prevent remaining extensions stopping.
      withdrawContributions(id)
      useViewerExtensionStore.getState().setLifecycle(id, 'failed', messageOf(error))
      return
    }

    withdrawContributions(id)
    useViewerExtensionStore.getState().setLifecycle(id, 'stopped')
  }

  /** FR-004: rejected visibly rather than silently ignored — a non-toggleable extension, or one
   * that is not currently started, cannot be activated. */
  activate(id: string, mode?: string): void {
    const extension = viewerExtensionRegistry.resolve(id)
    const store = useViewerExtensionStore.getState()

    if (!extension?.manifest.toggleable || store.extensions[id]?.lifecycle !== 'started') {
      store.setLifecycle(
        id,
        'failed',
        `Extension "${id}" cannot be activated: it is not a started, toggleable extension.`,
      )
      return
    }

    extension.activate?.(mode)
    store.setActivation(id, 'active')
  }

  deactivate(id: string): void {
    const extension = viewerExtensionRegistry.resolve(id)
    const store = useViewerExtensionStore.getState()
    if (!extension?.manifest.toggleable || store.extensions[id]?.lifecycle !== 'started') return

    extension.deactivate?.()
    store.setActivation(id, 'inactive')
  }

  private withTimeout(result: void | Promise<void>, timeoutMs: number): Promise<void> {
    if (!(result instanceof Promise)) return Promise.resolve()

    return new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`Extension start exceeded ${timeoutMs}ms.`)), timeoutMs)
      result.then(
        () => {
          clearTimeout(timer)
          resolve()
        },
        (error) => {
          clearTimeout(timer)
          reject(error)
        },
      )
    })
  }
}

/** Single module-level loader instance, mirroring every other viewer singleton's convention. */
export const viewerExtensionLoader = new ViewerExtensionLoader()
