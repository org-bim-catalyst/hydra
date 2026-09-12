import { beforeEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { panelTypeRegistry } from '../panels/registry'
import { useFloatingPanelStore } from '../panels/store/floatingPanelStore'
import { drawingSpaceRegistry } from '../scene/DrawingSpaceRegistry'
import type { ExtensionContext } from './context'
import { viewerExtensionLoader } from './loader'
import { viewerExtensionRegistry } from './registry'
import { useViewerExtensionStore } from './store/viewerExtensionStore'
import type { ViewerExtension } from './ViewerExtension'

const initialState = useViewerExtensionStore.getState()

function uniqueId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2)}`
}

function register(extension: Omit<ViewerExtension, 'id'> & { id?: string }): ViewerExtension {
  const full: ViewerExtension = { id: uniqueId('ext'), ...extension }
  viewerExtensionRegistry.register(full)
  return full
}

describe('viewerExtensionLoader', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  it('starts a registered extension, contributing through its context', async () => {
    const extension = register({
      manifest: { displayName: 'Test', description: 'test' },
      start: (context: ExtensionContext) => context.contributeOverlay(() => null),
      stop: () => {},
    })

    await viewerExtensionLoader.start(extension.id)

    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('started')
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(1)
  })

  it('FR-009: surfaces an unregistered declared id without throwing', async () => {
    const id = uniqueId('missing')
    await viewerExtensionLoader.start(id)

    const state = useViewerExtensionStore.getState().extensions[id]
    expect(state?.lifecycle).toBe('failed')
    expect(state?.failureReason).toMatch(/not registered/)
  })

  it('FR-008: starting an already-started extension is a no-op producing no duplicate contributions', async () => {
    const extension = register({
      manifest: { displayName: 'Test', description: 'test' },
      start: (context: ExtensionContext) => context.contributeOverlay(() => null),
      stop: () => {},
    })

    await viewerExtensionLoader.start(extension.id)
    await viewerExtensionLoader.start(extension.id)

    expect(useViewerExtensionStore.getState().contributions).toHaveLength(1)
  })

  it('FR-008: stopping a not-started extension is a no-op', async () => {
    const extension = register({
      manifest: { displayName: 'Test', description: 'test' },
      start: () => {},
      stop: () => {},
    })

    await viewerExtensionLoader.stop(extension.id)

    expect(useViewerExtensionStore.getState().extensions[extension.id]).toBeUndefined()
  })

  it('FR-011/FR-029: a throwing start is contained, recorded, and discards partial contributions', async () => {
    const extension = register({
      manifest: { displayName: 'Test', description: 'test' },
      start: (context: ExtensionContext) => {
        context.contributeOverlay(() => null)
        throw new Error('start blew up')
      },
      stop: () => {},
    })

    await viewerExtensionLoader.start(extension.id)

    const state = useViewerExtensionStore.getState().extensions[extension.id]
    expect(state?.lifecycle).toBe('failed')
    expect(state?.failureReason).toBe('start blew up')
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)
  })

  it('FR-012/FR-030: a throwing stop is surfaced and recorded, and does not block remaining extensions', async () => {
    const other = register({
      manifest: { displayName: 'Other', description: 'test' },
      start: () => {},
      stop: () => {},
    })
    const failing = register({
      manifest: { displayName: 'Failing', description: 'test' },
      start: () => {},
      stop: () => {
        throw new Error('stop blew up')
      },
    })

    await viewerExtensionLoader.start(other.id)
    await viewerExtensionLoader.start(failing.id)

    await viewerExtensionLoader.stop(failing.id)
    await viewerExtensionLoader.stop(other.id)

    expect(useViewerExtensionStore.getState().extensions[failing.id]?.lifecycle).toBe('failed')
    expect(useViewerExtensionStore.getState().extensions[failing.id]?.failureReason).toBe('stop blew up')
    expect(useViewerExtensionStore.getState().extensions[other.id]?.lifecycle).toBe('stopped')
  })

  it('FR-010: stopping while starting is honoured and discards whatever the in-flight start contributes', async () => {
    let resolveStart: () => void = () => {}
    const extension = register({
      manifest: { displayName: 'Slow', description: 'test' },
      start: (context: ExtensionContext) =>
        new Promise<void>((resolve) => {
          context.contributeOverlay(() => null)
          resolveStart = resolve
        }),
      stop: () => {},
    })

    const startPromise = viewerExtensionLoader.start(extension.id)
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('starting')

    await viewerExtensionLoader.stop(extension.id)
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('stopped')

    resolveStart()
    await startPromise

    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('stopped')
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)
  })

  it('research D4: a start exceeding its timeout is treated exactly as a throw', async () => {
    vi.useFakeTimers()
    try {
      const extension = register({
        manifest: { displayName: 'Hangs', description: 'test' },
        start: () => new Promise<void>(() => {}),
        stop: () => {},
      })

      const startPromise = viewerExtensionLoader.start(extension.id)
      await vi.advanceTimersByTimeAsync(5000)
      await startPromise

      const state = useViewerExtensionStore.getState().extensions[extension.id]
      expect(state?.lifecycle).toBe('failed')
      expect(state?.failureReason).toMatch(/exceeded/)
    } finally {
      vi.useRealTimers()
    }
  })

  it('FR-004: activating a non-toggleable extension is rejected visibly, not silently ignored', async () => {
    const extension = register({
      manifest: { displayName: 'Not toggleable', description: 'test' },
      start: () => {},
      stop: () => {},
      activate: vi.fn(),
    })
    await viewerExtensionLoader.start(extension.id)

    viewerExtensionLoader.activate(extension.id)

    expect(extension.activate).not.toHaveBeenCalled()
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('failed')
  })

  it('activates and deactivates a toggleable, started extension', async () => {
    const activate = vi.fn()
    const deactivate = vi.fn()
    const extension = register({
      manifest: { displayName: 'Toggleable', description: 'test', toggleable: true },
      start: () => {},
      stop: () => {},
      activate,
      deactivate,
    })
    await viewerExtensionLoader.start(extension.id)
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.activation).toBe('inactive')

    viewerExtensionLoader.activate(extension.id, 'mode-a')
    expect(activate).toHaveBeenCalledWith('mode-a')
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.activation).toBe('active')
    // US4: the two axes are independent — activating never changes lifecycle.
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('started')

    viewerExtensionLoader.deactivate(extension.id)
    expect(deactivate).toHaveBeenCalled()
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.activation).toBe('inactive')
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.lifecycle).toBe('started')
  })

  it('US4/data-model.md: a stopped toggleable extension is neither active nor inactive', async () => {
    const extension = register({
      manifest: { displayName: 'Toggleable', description: 'test', toggleable: true },
      start: () => {},
      stop: () => {},
      activate: () => {},
      deactivate: () => {},
    })
    await viewerExtensionLoader.start(extension.id)
    viewerExtensionLoader.activate(extension.id)
    expect(useViewerExtensionStore.getState().extensions[extension.id]?.activation).toBe('active')

    await viewerExtensionLoader.stop(extension.id)

    expect(useViewerExtensionStore.getState().extensions[extension.id]?.activation).toBeNull()
  })

  it('US3/T044: one extension\'s start failure leaves every other extension started', async () => {
    const good = register({ manifest: { displayName: 'Good', description: 'test' }, start: () => {}, stop: () => {} })
    const bad = register({
      manifest: { displayName: 'Bad', description: 'test' },
      start: () => {
        throw new Error('boom')
      },
      stop: () => {},
    })

    await viewerExtensionLoader.start(good.id)
    await viewerExtensionLoader.start(bad.id)

    expect(useViewerExtensionStore.getState().extensions[good.id]?.lifecycle).toBe('started')
    expect(useViewerExtensionStore.getState().extensions[bad.id]?.lifecycle).toBe('failed')
  })

  it('US3/T044: one extension\'s stop failure leaves every other extension stopped', async () => {
    const good = register({ manifest: { displayName: 'Good', description: 'test' }, start: () => {}, stop: () => {} })
    const bad = register({
      manifest: { displayName: 'Bad', description: 'test' },
      start: () => {},
      stop: () => {
        throw new Error('boom')
      },
    })

    await viewerExtensionLoader.start(good.id)
    await viewerExtensionLoader.start(bad.id)
    await viewerExtensionLoader.stop(bad.id)
    await viewerExtensionLoader.stop(good.id)

    expect(useViewerExtensionStore.getState().extensions[bad.id]?.lifecycle).toBe('failed')
    expect(useViewerExtensionStore.getState().extensions[good.id]?.lifecycle).toBe('stopped')
  })

  it('US3/T044: an unknown declared id neither blocks nor fails the extensions around it', async () => {
    const before = register({ manifest: { displayName: 'Before', description: 'test' }, start: () => {}, stop: () => {} })
    const after = register({ manifest: { displayName: 'After', description: 'test' }, start: () => {}, stop: () => {} })
    const missingId = uniqueId('missing')

    await viewerExtensionLoader.start(before.id)
    await viewerExtensionLoader.start(missingId)
    await viewerExtensionLoader.start(after.id)

    expect(useViewerExtensionStore.getState().extensions[before.id]?.lifecycle).toBe('started')
    expect(useViewerExtensionStore.getState().extensions[after.id]?.lifecycle).toBe('started')
    expect(useViewerExtensionStore.getState().extensions[missingId]?.lifecycle).toBe('failed')
  })

  it('T051b/FR-016: starting or stopping one extension leaves every other extension\'s contributions and behaviour untouched (the ordinary case, not a failure)', async () => {
    const a = register({
      manifest: { displayName: 'A', description: 'test' },
      start: (context: ExtensionContext) => context.contributeOverlay(() => null),
      stop: () => {},
    })
    const b = register({
      manifest: { displayName: 'B', description: 'test' },
      start: (context: ExtensionContext) => context.contributeOverlay(() => null),
      stop: () => {},
    })

    await viewerExtensionLoader.start(a.id)
    await viewerExtensionLoader.start(b.id)
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(2)

    await viewerExtensionLoader.stop(a.id)

    expect(useViewerExtensionStore.getState().extensions[a.id]?.lifecycle).toBe('stopped')
    expect(useViewerExtensionStore.getState().extensions[b.id]?.lifecycle).toBe('started')
    const remaining = useViewerExtensionStore.getState().contributions
    expect(remaining).toHaveLength(1)
    expect(remaining[0].extensionId).toBe(b.id)
  })

  it('T052/SC-004/quickstart Scenario 5: fifty start/stop cycles of an extension contributing every kind accumulate nothing', async () => {
    const typeKey = uniqueId('cycle-kind')
    let eventFireCount = 0
    const extension = register({
      manifest: { displayName: 'Cycling', description: 'test' },
      start: (context: ExtensionContext) => {
        context.contributeOverlay(() => null)
        context.contributeToolbarEntry({ id: 'cycle-entry', label: 'Cycle', icon: () => null, onClick: () => {} })
        context.registerLivePanelKind({
          typeKey,
          renderer: () => null,
          schema: z.object({}),
          chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
        })
        context.on('rotationChanged', () => {
          eventFireCount += 1
        })
      },
      stop: () => {},
    })

    for (let cycle = 0; cycle < 50; cycle++) {
      await viewerExtensionLoader.start(extension.id)
      await viewerExtensionLoader.stop(extension.id)
    }

    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)
    expect(panelTypeRegistry.resolve(typeKey)).toBeUndefined()

    // No orphaned subscription: the event bus must not still be holding fifty stale handlers.
    const { viewerEngine } = await import('../engine/viewerEngineInstance')
    viewerEngine.setRotationEnabled(true)
    expect(eventFireCount).toBe(0)
  })

  it('FR-036: stopping an extension unregisters its live panel kind and marks an open panel of it unavailable', async () => {
    const typeKey = uniqueId('kind')
    const initialPanelState = useFloatingPanelStore.getState()
    try {
      const extension = register({
        manifest: { displayName: 'Live kind', description: 'test' },
        start: (context: ExtensionContext) =>
          context.registerLivePanelKind({
            typeKey,
            renderer: () => null,
            schema: z.object({ label: z.string() }),
            chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
          }),
        stop: () => {},
      })

      await viewerExtensionLoader.start(extension.id)
      useFloatingPanelStore.getState().openPanel({
        kind: 'live',
        requestId: 'open-1',
        typeKey,
        title: 'Test',
        data: { label: 'hi' },
      })
      expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('valid')

      await viewerExtensionLoader.stop(extension.id)

      expect(panelTypeRegistry.resolve(typeKey)).toBeUndefined()
      expect(useFloatingPanelStore.getState().panels[0].validationStatus).toBe('unknown-type')
    } finally {
      useFloatingPanelStore.setState(initialPanelState, true)
    }
  })

  it('T021: stopping an extension releases its drawing space and clears its frame subscriptions, leaving other extensions untouched', async () => {
    let framesFired = 0
    const drawer = register({
      manifest: { displayName: 'Drawer', description: 'test' },
      start: (context: ExtensionContext) => {
        context.acquireDrawingSpace().onFrame(() => {
          framesFired += 1
        })
      },
      stop: () => {},
    })
    const other = register({
      manifest: { displayName: 'Other', description: 'test' },
      start: (context: ExtensionContext) => {
        context.acquireDrawingSpace()
      },
      stop: () => {},
    })

    await viewerExtensionLoader.start(drawer.id)
    await viewerExtensionLoader.start(other.id)
    drawingSpaceRegistry.invokeFrameCallbacks(0.016)
    expect(framesFired).toBe(1)

    await viewerExtensionLoader.stop(drawer.id)
    drawingSpaceRegistry.invokeFrameCallbacks(0.016)

    expect(framesFired).toBe(1)
    expect(useViewerExtensionStore.getState().extensions[other.id]?.lifecycle).toBe('started')
  })
})
