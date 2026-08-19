import { beforeEach, describe, expect, it } from 'vitest'
import { useActiveConversationStore } from './activeConversationStore'

describe('activeConversationStore', () => {
  beforeEach(() => {
    sessionStorage.clear()
    useActiveConversationStore.setState({ activeChatId: null })
  })

  it('defaults to no active conversation', () => {
    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
  })

  it('setActiveChatId updates state and persists to sessionStorage', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-123')

    expect(useActiveConversationStore.getState().activeChatId).toBe('chat-123')
    const stored = sessionStorage.getItem('ask-lucy-active-conversation')
    expect(stored).not.toBeNull()
    expect(JSON.parse(stored!).state.activeChatId).toBe('chat-123')
  })

  it('setActiveChatId(null) clears the active conversation', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-123')
    useActiveConversationStore.getState().setActiveChatId(null)

    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
  })

  it('does not persist to localStorage (session-only, per data-model.md)', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-123')
    expect(localStorage.getItem('ask-lucy-active-conversation')).toBeNull()
  })
})
