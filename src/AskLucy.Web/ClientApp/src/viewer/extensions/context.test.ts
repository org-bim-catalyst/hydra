import { beforeEach, describe, expect, it } from 'vitest'
import { z } from 'zod'
import { viewerEngine } from '../engine/viewerEngineInstance'
import { panelTypeRegistry } from '../panels/registry'
import { useFloatingPanelStore } from '../panels/store/floatingPanelStore'
import { createExtensionContext } from './context'
import { useViewerExtensionStore } from './store/viewerExtensionStore'

const initialExtensionState = useViewerExtensionStore.getState()
const initialPanelState = useFloatingPanelStore.getState()

describe('createExtensionContext', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialExtensionState, true)
    useFloatingPanelStore.setState(initialPanelState, true)
  })

  it('passes the published viewer engine through unchanged', () => {
    const context = createExtensionContext('x')
    expect(context.engine).toBe(viewerEngine)
  })

  it('records contributeOverlay against the calling extension', () => {
    const Component = () => null
    createExtensionContext('ext-a').contributeOverlay(Component)

    const contributions = useViewerExtensionStore.getState().contributions
    expect(contributions).toEqual([{ kind: 'overlay', extensionId: 'ext-a', component: Component }])
  })

  it('records contributeToolbarEntry against the calling extension', () => {
    const entry = { id: 'entry-1', label: 'Test', icon: () => null, onClick: () => {} }
    createExtensionContext('ext-b').contributeToolbarEntry(entry)

    const contributions = useViewerExtensionStore.getState().contributions
    expect(contributions).toEqual([{ kind: 'toolbarEntry', extensionId: 'ext-b', entry }])
  })

  it('registers a live panel kind in the panel registry and records the contribution', () => {
    const typeKey = `context-test-${Math.random()}`
    const definition = {
      typeKey,
      renderer: () => null,
      schema: z.object({ label: z.string() }),
      chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
    }

    createExtensionContext('ext-c').registerLivePanelKind(definition)

    expect(panelTypeRegistry.resolve(typeKey)).toBe(definition)
    const contributions = useViewerExtensionStore.getState().contributions
    expect(contributions).toEqual([{ kind: 'livePanelKind', extensionId: 'ext-c', typeKey }])

    panelTypeRegistry.unregister(typeKey)
  })

  it('opens a panel without recording a contribution', () => {
    const typeKey = `context-open-${Math.random()}`
    panelTypeRegistry.register({
      typeKey,
      renderer: () => null,
      schema: z.object({ label: z.string() }),
      chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
    })

    createExtensionContext('ext-d').openPanel({
      kind: 'live',
      requestId: 'r1',
      typeKey,
      title: 'Test',
      data: { label: 'hi' },
    })

    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)

    panelTypeRegistry.unregister(typeKey)
  })

  it('records an unsubscribe as an eventSubscription contribution when subscribing to an event', () => {
    createExtensionContext('ext-e').on('selectionChanged', () => {})

    const contributions = useViewerExtensionStore.getState().contributions
    expect(contributions).toHaveLength(1)
    expect(contributions[0].kind).toBe('eventSubscription')
    expect(contributions[0].extensionId).toBe('ext-e')
  })

  it('contains a throwing handler without stopping delivery to another subscriber, and surfaces the failure', () => {
    let otherReceived = false

    // The loader records lifecycle state before an extension's handlers can run; a raw
    // `extensions[id]` entry is what recordEventFailure updates (store contract: it never
    // fabricates an entry for an id it has never tracked).
    useViewerExtensionStore.getState().setLifecycle('thrower', 'started')

    createExtensionContext('thrower').on('rotationChanged', () => {
      throw new Error('boom')
    })
    createExtensionContext('listener').on('rotationChanged', () => {
      otherReceived = true
    })

    viewerEngine.setRotationEnabled(true)

    expect(otherReceived).toBe(true)
    expect(useViewerExtensionStore.getState().extensions.thrower?.lastEventError).toBe('boom')
  })

  it('a panel opened by an extension is not tracked for teardown and survives that extension stopping (contract: "a panel the user can close is theirs, not the extension\'s")', () => {
    const typeKey = `context-survive-${Math.random()}`
    panelTypeRegistry.register({
      typeKey,
      renderer: () => null,
      schema: z.object({ label: z.string() }),
      chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
    })

    createExtensionContext('ext-f').openPanel({
      kind: 'live',
      requestId: 'survives',
      typeKey,
      title: 'Test',
      data: { label: 'hi' },
    })
    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)

    // No contribution was recorded for this panel, so withdrawing ext-f's contributions
    // (what stop() does) has nothing to withdraw for it, and the panel is untouched.
    useViewerExtensionStore.getState().removeContributionsFor('ext-f')

    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)
    expect(useFloatingPanelStore.getState().panels[0].id).toBe('survives')

    panelTypeRegistry.unregister(typeKey)
  })

  it('never requires the extension to pass its own id to any helper', () => {
    const context = createExtensionContext('self-contained')
    expect(context.contributeOverlay.length).toBe(1)
    expect(context.contributeToolbarEntry.length).toBe(1)
    expect(context.registerLivePanelKind.length).toBe(1)
    expect(context.openPanel.length).toBe(1)
  })
})
