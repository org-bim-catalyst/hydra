import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerExtensionStore } from './viewerExtensionStore'

const initialState = useViewerExtensionStore.getState()

describe('viewerExtensionStore', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
  })

  it('sets lifecycle state for an extension', () => {
    useViewerExtensionStore.getState().setLifecycle('viewer.poi-marker', 'starting')
    expect(useViewerExtensionStore.getState().extensions['viewer.poi-marker']).toEqual({
      lifecycle: 'starting',
      failureReason: null,
      activation: null,
      lastEventError: null,
    })
  })

  it('carries a failure reason only when lifecycle is failed', () => {
    useViewerExtensionStore.getState().setLifecycle('x', 'failed', 'boom')
    expect(useViewerExtensionStore.getState().extensions.x.failureReason).toBe('boom')

    useViewerExtensionStore.getState().setLifecycle('x', 'started')
    expect(useViewerExtensionStore.getState().extensions.x.failureReason).toBeNull()
  })

  it('preserves activation state across a transition that stays started', () => {
    useViewerExtensionStore.getState().setLifecycle('x', 'started')
    useViewerExtensionStore.getState().setActivation('x', 'active')
    useViewerExtensionStore.getState().setLifecycle('x', 'started')
    expect(useViewerExtensionStore.getState().extensions.x.activation).toBe('active')
  })

  it('data-model.md "a stopped extension is neither" — resets activation to null once lifecycle leaves started', () => {
    useViewerExtensionStore.getState().setLifecycle('x', 'started')
    useViewerExtensionStore.getState().setActivation('x', 'active')
    useViewerExtensionStore.getState().setLifecycle('x', 'stopped')
    expect(useViewerExtensionStore.getState().extensions.x.activation).toBeNull()
  })

  it('setActivation is a no-op for an extension with no recorded state', () => {
    useViewerExtensionStore.getState().setActivation('does-not-exist', 'active')
    expect(useViewerExtensionStore.getState().extensions['does-not-exist']).toBeUndefined()
  })

  it('records a contribution against its extension id', () => {
    useViewerExtensionStore.getState().addContribution({ kind: 'overlay', extensionId: 'x', component: () => null })
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(1)
    expect(useViewerExtensionStore.getState().contributions[0].extensionId).toBe('x')
  })

  it('preserves insertion order across contributions from different extensions', () => {
    const store = useViewerExtensionStore.getState()
    store.addContribution({ kind: 'toolbarEntry', extensionId: 'a', entry: { id: 'a-1', label: 'A', icon: () => null, onClick: () => {} } })
    store.addContribution({ kind: 'toolbarEntry', extensionId: 'b', entry: { id: 'b-1', label: 'B', icon: () => null, onClick: () => {} } })
    const ids = useViewerExtensionStore.getState().contributions.map((c) => c.extensionId)
    expect(ids).toEqual(['a', 'b'])
  })

  it('removes only the contributions belonging to the given extension', () => {
    const store = useViewerExtensionStore.getState()
    store.addContribution({ kind: 'overlay', extensionId: 'a', component: () => null })
    store.addContribution({ kind: 'overlay', extensionId: 'b', component: () => null })

    store.removeContributionsFor('a')

    const remaining = useViewerExtensionStore.getState().contributions
    expect(remaining).toHaveLength(1)
    expect(remaining[0].extensionId).toBe('b')
  })

  it('removeContributionsFor is a no-op for an extension with no contributions', () => {
    useViewerExtensionStore.getState().addContribution({ kind: 'overlay', extensionId: 'a', component: () => null })
    useViewerExtensionStore.getState().removeContributionsFor('never-contributed')
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(1)
  })
})
