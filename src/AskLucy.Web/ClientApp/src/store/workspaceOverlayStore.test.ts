import { beforeEach, describe, expect, it } from 'vitest'
import { useWorkspaceOverlayStore } from './workspaceOverlayStore'

function resetStore() {
  useWorkspaceOverlayStore.setState({
    expandedControlId: null,
    viewMode: 'isometric',
    unreadControlIds: new Set(),
  })
}

describe('workspaceOverlayStore', () => {
  beforeEach(() => {
    resetStore()
  })

  it('defaults to nothing expanded and isometric view mode', () => {
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBeNull()
    expect(useWorkspaceOverlayStore.getState().viewMode).toBe('isometric')
  })

  it('expand(id) sets the expanded control and clears its unread flag', () => {
    useWorkspaceOverlayStore.getState().markUnread('chat')
    useWorkspaceOverlayStore.getState().expand('chat')
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBe('chat')
    expect(useWorkspaceOverlayStore.getState().unreadControlIds.has('chat')).toBe(false)
  })

  it('expanding a new control collapses whatever was previously expanded (FR-015)', () => {
    useWorkspaceOverlayStore.getState().expand('layers')
    useWorkspaceOverlayStore.getState().expand('chat')
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBe('chat')
  })

  it('collapse() clears the expanded control', () => {
    useWorkspaceOverlayStore.getState().expand('chat')
    useWorkspaceOverlayStore.getState().collapse()
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBeNull()
  })

  it('toggle(id) expands when collapsed and collapses when already expanded', () => {
    useWorkspaceOverlayStore.getState().toggle('chat')
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBe('chat')
    useWorkspaceOverlayStore.getState().toggle('chat')
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBeNull()
  })

  it('toggle(id) switches to a different control instead of collapsing it', () => {
    useWorkspaceOverlayStore.getState().toggle('layers')
    useWorkspaceOverlayStore.getState().toggle('chat')
    expect(useWorkspaceOverlayStore.getState().expandedControlId).toBe('chat')
  })

  it('setViewMode(mode) updates the current view mode', () => {
    useWorkspaceOverlayStore.getState().setViewMode('plan')
    expect(useWorkspaceOverlayStore.getState().viewMode).toBe('plan')
  })

  it('markUnread(id) flags a control as having unseen activity while collapsed', () => {
    useWorkspaceOverlayStore.getState().markUnread('chat')
    expect(useWorkspaceOverlayStore.getState().unreadControlIds.has('chat')).toBe(true)
  })

  it('does not persist across a fresh store instance (no persist middleware)', () => {
    // Unlike assistantPanelStore (zustand/middleware persist under 'ask-lucy-assistant-panel'),
    // this store writes nothing to localStorage — verifying no such key exists is a reasonable
    // proxy for "session-only" (data-model.md).
    expect(localStorage.getItem('ask-lucy-workspace-overlay')).toBeNull()
  })
})
