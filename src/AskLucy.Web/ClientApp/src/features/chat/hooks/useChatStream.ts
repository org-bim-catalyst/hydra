import { useCallback, useEffect, useRef, useState } from 'react'
import * as chatsApi from '../api/chatsApi'
import type { PersistedMessage } from '../api/chatsApi'
import { generateImage, streamChat, translate, type ChatMessage } from '../api/aiApi'

const TITLE_MAX_LENGTH = 60

function toTitle(text: string): string {
  return text.length > TITLE_MAX_LENGTH ? `${text.slice(0, TITLE_MAX_LENGTH)}…` : text
}

function toChatMessages(persisted: PersistedMessage[]): ChatMessage[] {
  return persisted.map((m) => ({
    role: m.role === 'User' ? 'user' : 'assistant',
    content: m.kind === 'Image' ? `![${m.sourceText ?? 'Generated image'}](${m.content})` : m.content,
  }))
}

/**
 * Drives one conversation's messages, matching a ChatGPT-style history experience
 * (2026-07-28 decision — see AppendMessageCommand's doc comment). `chatId`/`initialMessages`
 * seed this hook once per mount (the caller remounts it, via a `key`, when the user
 * explicitly switches conversations) — a chat created mid-session via `ensureChatId` updates
 * this hook's own internal notion of the active chat without needing a remount, so an
 * in-flight stream is never interrupted by the id changing out from under it.
 */
export function useChatStream(
  chatId: string | null,
  initialMessages: PersistedMessage[] | undefined,
  onChatCreated: (id: string) => void,
) {
  const [messages, setMessages] = useState<ChatMessage[]>(() => (initialMessages ? toChatMessages(initialMessages) : []))
  const [isStreaming, setIsStreaming] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)
  const chatIdRef = useRef<string | null>(chatId)
  const initializedRef = useRef(initialMessages !== undefined)

  // Guards every state update below against a stale completion — if the user switches to a
  // different chat (or starts a new one) while a message is still sending, this view
  // unmounts but the in-flight send() keeps running (JS doesn't cancel promises on unmount).
  // Without this guard, its onChatCreated/setMessages calls would still land on — and
  // corrupt — whichever chat the user has since navigated to.
  const isActiveRef = useRef(true)
  useEffect(() => {
    isActiveRef.current = true
    return () => {
      isActiveRef.current = false
    }
  }, [])

  useEffect(() => {
    if (!initializedRef.current && initialMessages !== undefined) {
      setMessages(toChatMessages(initialMessages))
      initializedRef.current = true
    }
  }, [initialMessages])

  const ensureChatId = useCallback(
    async (titleHint: string) => {
      if (chatIdRef.current) return chatIdRef.current
      const chat = await chatsApi.createChat(toTitle(titleHint))
      chatIdRef.current = chat.id
      // Only tell the parent about this if the user is still looking at this view — if
      // they've since switched away, this chat's creation still succeeded (it'll show up
      // next time the chat list refetches), but it must not hijack whatever chat is now
      // selected.
      if (isActiveRef.current) {
        onChatCreated(chat.id)
      }
      return chat.id
    },
    [onChatCreated],
  )

  const send = useCallback(
    async (content: string) => {
      // Once the user has started actively chatting in this view, the persisted-messages
      // fetch triggered below (by ensureChatId changing the chatId prop) must never be
      // allowed to seed/overwrite local state again — it's racing the still-in-progress
      // send, and can resolve with an incomplete snapshot (the assistant's reply is only
      // persisted after the full stream finishes) that would otherwise wipe out the
      // conversation that's live on screen right now.
      initializedRef.current = true

      const userMessage: ChatMessage = { role: 'user', content }
      const history = [...messages, userMessage]
      setMessages([...history, { role: 'assistant', content: '' }])
      setIsStreaming(true)
      setError(null)

      const controller = new AbortController()
      abortRef.current = controller

      try {
        const activeChatId = await ensureChatId(content)
        let assistantContent = ''
        for await (const chunk of streamChat(activeChatId, history, controller.signal)) {
          assistantContent += chunk
          if (isActiveRef.current) {
            setMessages([...history, { role: 'assistant', content: assistantContent }])
          }
        }
      } catch (err) {
        if (isActiveRef.current) {
          // Drop the empty placeholder assistant bubble rather than leaving it stuck
          // on-screen forever with no content and no explanation.
          setMessages(history)
          setError(err instanceof Error ? err.message : 'Failed to send message. Please try again.')
        }
      } finally {
        setIsStreaming(false)
        abortRef.current = null
      }
    },
    [messages, ensureChatId],
  )

  const sendImage = useCallback(
    async (prompt: string) => {
      initializedRef.current = true
      const activeChatId = await ensureChatId(prompt)
      const url = await generateImage(activeChatId, prompt)
      if (isActiveRef.current) {
        setMessages((prev) => [...prev, { role: 'user', content: prompt }, { role: 'assistant', content: `![${prompt}](${url})` }])
      }
      return url
    },
    [ensureChatId],
  )

  const sendTranslation = useCallback(
    async (text: string, targetLanguage: string) => {
      initializedRef.current = true
      const activeChatId = await ensureChatId(text)
      const html = await translate(activeChatId, text, targetLanguage)
      const container = document.createElement('div')
      container.innerHTML = html
      const plain = container.textContent ?? ''
      if (isActiveRef.current) {
        setMessages((prev) => [...prev, { role: 'user', content: text }, { role: 'assistant', content: plain }])
      }
      return plain
    },
    [ensureChatId],
  )

  const stop = useCallback(() => abortRef.current?.abort(), [])
  const clearError = useCallback(() => setError(null), [])

  return { messages, isStreaming, error, clearError, send, sendImage, sendTranslation, stop }
}
