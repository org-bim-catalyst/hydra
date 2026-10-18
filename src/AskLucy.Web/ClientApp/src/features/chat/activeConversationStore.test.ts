import { beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '../../store/authStore'
import { useActiveConversationStore } from './activeConversationStore'

const remembered = () => JSON.parse(localStorage.getItem('ask-lucy-last-conversation')!).state.lastChatByUser

describe('activeConversationStore', () => {
  beforeEach(() => {
    localStorage.clear()
    useAuthStore.setState({ userId: 'user-a' })
    useActiveConversationStore.setState({ activeChatId: null, lastChatByUser: {} })
  })

  it('defaults to no active conversation', () => {
    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
  })

  it('setActiveChatId updates the open conversation and remembers it for the signed-in account', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-123')

    expect(useActiveConversationStore.getState().activeChatId).toBe('chat-123')
    expect(remembered()).toEqual({ 'user-a': 'chat-123' })
  })

  it('setActiveChatId(null) clears the open conversation but keeps the remembered one', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-123')
    useActiveConversationStore.getState().setActiveChatId(null)

    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
    expect(remembered()).toEqual({ 'user-a': 'chat-123' })
  })

  it('resumes the account\'s last conversation when none is open, and never another account\'s', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-a')
    useActiveConversationStore.getState().setActiveChatId(null)

    expect(useActiveConversationStore.getState().resumeChatId()).toBe('chat-a')

    useAuthStore.setState({ userId: 'user-b' })
    expect(useActiveConversationStore.getState().resumeChatId()).toBeNull()
  })

  it('keeps each account\'s own last conversation', () => {
    useActiveConversationStore.getState().setActiveChatId('chat-a')
    useAuthStore.setState({ userId: 'user-b' })
    useActiveConversationStore.getState().setActiveChatId('chat-b')

    expect(remembered()).toEqual({ 'user-a': 'chat-a', 'user-b': 'chat-b' })
  })
})
