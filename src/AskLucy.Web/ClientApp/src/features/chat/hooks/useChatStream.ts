import { useCallback, useEffect, useRef, useState } from 'react'
import * as chatsApi from '../api/chatsApi'
import type { PersistedMessage } from '../api/chatsApi'
import { generateImage, streamChat, translate, type ChatMessage, type GenerationParameters } from '../api/aiApi'

const TITLE_MAX_LENGTH = 60

function toTitle(text: string): string {
  return text.length > TITLE_MAX_LENGTH ? `${text.slice(0, TITLE_MAX_LENGTH)}…` : text
}

function toChatMessages(persisted: PersistedMessage[]): ChatMessage[] {
  return persisted.map((m) => ({
    role: m.role === 'User' ? 'user' : 'assistant',
    content: m.kind === 'Image' ? `![${m.sourceText ?? 'Generated image'}](${m.content})` : m.content,
    provider: m.provider,
    model: m.model,
    attachments: m.attachments,
    citations: m.citations,
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
  // Tracks whether the *user* has sent anything in this specific mounted view — not whether
  // initialMessages has ever been defined. A brand-new chat's first messages fetch races an
  // in-progress reply and can resolve empty; that empty result is still "defined", so gating
  // on definedness alone would permanently block a later, corrected refetch from ever being
  // applied (the "new chat blank on return" bug — see research.md Topic 6). This flag flips
  // exactly once, inside send/sendImage/sendTranslation, and never reverses within this mount.
  const hasSentRef = useRef(false)
  // Last user-message content actually attempted via send() — lets retry() (FR-008) resend
  // exactly what failed without the caller needing to remember/re-supply it.
  const lastAttemptedContentRef = useRef<string | null>(null)

  // specs/005-multi-provider-ai-engine FR-008/FR-009 — the conversation's current
  // provider/model selection. Not yet seeded from the persisted `UserChat.ProviderId`/
  // `ModelId` on load (that would need those fields threaded onto `UserChatDto` too) — the
  // caller (ChatPage) is expected to default this from the enabled-provider catalog.
  const [providerId, setProviderId] = useState<string | null>(null)
  const [modelId, setModelId] = useState<string | null>(null)

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
    if (!hasSentRef.current && initialMessages !== undefined) {
      setMessages(toChatMessages(initialMessages))
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

  /** FR-009: persists the new selection so it applies to messages sent after this call (FR-011 leaves prior messages' attribution untouched). No-op on the backend until a chat actually exists. */
  const setSelection = useCallback(
    (newProviderId: string, newModelId: string, generationParameters?: GenerationParameters) => {
      setProviderId(newProviderId)
      setModelId(newModelId)
      if (chatIdRef.current) {
        chatsApi.updateChatModelSelection(chatIdRef.current, newProviderId, newModelId, generationParameters).catch((err: unknown) => {
          setError(err instanceof Error ? err.message : 'Failed to save the model selection.')
        })
      }
    },
    [],
  )

  const send = useCallback(
    async (content: string) => {
      if (!providerId || !modelId) {
        setError('Choose an AI provider and model before sending a message.')
        return
      }
      // Once the user has started actively chatting in this view, the persisted-messages
      // fetch triggered below (by ensureChatId changing the chatId prop) must never be
      // allowed to seed/overwrite local state again — it's racing the still-in-progress
      // send, and can resolve with an incomplete snapshot (the assistant's reply is only
      // persisted after the full stream finishes) that would otherwise wipe out the
      // conversation that's live on screen right now.
      hasSentRef.current = true
      lastAttemptedContentRef.current = content

      const userMessage: ChatMessage = { role: 'user', content }
      const history = [...messages, userMessage]
      setMessages([...history, { role: 'assistant', content: '' }])
      setIsStreaming(true)
      setError(null)

      const controller = new AbortController()
      abortRef.current = controller

      let assistantContent = ''
      try {
        const activeChatId = await ensureChatId(content)
        for await (const chunk of streamChat(activeChatId, history, providerId, modelId, undefined, controller.signal)) {
          assistantContent += chunk
          if (isActiveRef.current) {
            setMessages([...history, { role: 'assistant', content: assistantContent }])
          }
        }
      } catch (err) {
        if (isActiveRef.current) {
          // FR-030: keep whatever partial content already streamed in (flagged incomplete)
          // rather than discarding it — a connection drop mid-stream shouldn't erase a
          // reply the user could already see arriving.
          setMessages([...history, { role: 'assistant', content: assistantContent, isIncomplete: true }])
          setError(err instanceof Error ? err.message : 'Failed to send message. Please try again.')
        }
      } finally {
        setIsStreaming(false)
        abortRef.current = null
      }
    },
    [messages, ensureChatId, providerId, modelId],
  )

  const sendImage = useCallback(
    async (prompt: string) => {
      hasSentRef.current = true
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
      hasSentRef.current = true
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

  /** Resends the message content from the most recent failed send() (FR-008). No-op if nothing has failed. */
  const retry = useCallback(() => {
    if (lastAttemptedContentRef.current !== null) {
      void send(lastAttemptedContentRef.current)
    }
  }, [send])

  return { messages, isStreaming, error, clearError, send, sendImage, sendTranslation, stop, retry, providerId, modelId, setSelection }
}
