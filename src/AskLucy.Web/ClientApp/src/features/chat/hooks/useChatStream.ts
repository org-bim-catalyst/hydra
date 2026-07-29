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
  const abortRef = useRef<AbortController | null>(null)
  const chatIdRef = useRef<string | null>(chatId)
  const initializedRef = useRef(initialMessages !== undefined)

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
      onChatCreated(chat.id)
      return chat.id
    },
    [onChatCreated],
  )

  const send = useCallback(
    async (content: string) => {
      const userMessage: ChatMessage = { role: 'user', content }
      const history = [...messages, userMessage]
      setMessages([...history, { role: 'assistant', content: '' }])
      setIsStreaming(true)

      const controller = new AbortController()
      abortRef.current = controller

      try {
        const activeChatId = await ensureChatId(content)
        let assistantContent = ''
        for await (const chunk of streamChat(activeChatId, history, controller.signal)) {
          assistantContent += chunk
          setMessages([...history, { role: 'assistant', content: assistantContent }])
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
      const activeChatId = await ensureChatId(prompt)
      const url = await generateImage(activeChatId, prompt)
      setMessages((prev) => [...prev, { role: 'user', content: prompt }, { role: 'assistant', content: `![${prompt}](${url})` }])
      return url
    },
    [ensureChatId],
  )

  const sendTranslation = useCallback(
    async (text: string, targetLanguage: string) => {
      const activeChatId = await ensureChatId(text)
      const html = await translate(activeChatId, text, targetLanguage)
      const container = document.createElement('div')
      container.innerHTML = html
      const plain = container.textContent ?? ''
      setMessages((prev) => [...prev, { role: 'user', content: text }, { role: 'assistant', content: plain }])
      return plain
    },
    [ensureChatId],
  )

  const stop = useCallback(() => abortRef.current?.abort(), [])

  return { messages, isStreaming, send, sendImage, sendTranslation, stop }
}
