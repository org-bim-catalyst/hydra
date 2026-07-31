import { beforeEach, describe, expect, it } from 'vitest'
import { useAssistantPanelStore } from './assistantPanelStore'

function resetStore() {
  useAssistantPanelStore.setState({ isOpen: true, hasUnreadWhileCollapsed: false })
}

describe('assistantPanelStore', () => {
  beforeEach(() => {
    resetStore()
  })

  it('defaults to open with no unread indicator', () => {
    expect(useAssistantPanelStore.getState().isOpen).toBe(true)
    expect(useAssistantPanelStore.getState().hasUnreadWhileCollapsed).toBe(false)
  })

  it('close() collapses the panel', () => {
    useAssistantPanelStore.getState().close()
    expect(useAssistantPanelStore.getState().isOpen).toBe(false)
  })

  it('markUnread() sets the unread flag (FR-016)', () => {
    useAssistantPanelStore.getState().close()
    useAssistantPanelStore.getState().markUnread()
    expect(useAssistantPanelStore.getState().hasUnreadWhileCollapsed).toBe(true)
  })

  it('open() clears the unread flag', () => {
    useAssistantPanelStore.setState({ isOpen: false, hasUnreadWhileCollapsed: true })
    useAssistantPanelStore.getState().open()
    expect(useAssistantPanelStore.getState().isOpen).toBe(true)
    expect(useAssistantPanelStore.getState().hasUnreadWhileCollapsed).toBe(false)
  })

  it('toggle() from collapsed opens and clears the unread flag', () => {
    useAssistantPanelStore.setState({ isOpen: false, hasUnreadWhileCollapsed: true })
    useAssistantPanelStore.getState().toggle()
    expect(useAssistantPanelStore.getState().isOpen).toBe(true)
    expect(useAssistantPanelStore.getState().hasUnreadWhileCollapsed).toBe(false)
  })

  it('toggle() from open collapses the panel', () => {
    useAssistantPanelStore.setState({ isOpen: true, hasUnreadWhileCollapsed: false })
    useAssistantPanelStore.getState().toggle()
    expect(useAssistantPanelStore.getState().isOpen).toBe(false)
  })
})
