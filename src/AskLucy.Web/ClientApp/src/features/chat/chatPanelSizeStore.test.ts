import { beforeEach, describe, expect, it } from 'vitest'
import { useChatPanelSizeStore } from './chatPanelSizeStore'

describe('chatPanelSizeStore', () => {
  beforeEach(() => {
    localStorage.clear()
    useChatPanelSizeStore.setState({ isFullHeight: false })
  })

  it('defaults to half-height', () => {
    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(false)
  })

  it('toggle() flips isFullHeight and persists to localStorage', () => {
    useChatPanelSizeStore.getState().toggle()

    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(true)
    const stored = localStorage.getItem('ask-lucy-chat-panel-size')
    expect(stored).not.toBeNull()
    expect(JSON.parse(stored!).state.isFullHeight).toBe(true)
  })

  it('toggle() twice returns to half-height', () => {
    useChatPanelSizeStore.getState().toggle()
    useChatPanelSizeStore.getState().toggle()

    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(false)
  })
})
