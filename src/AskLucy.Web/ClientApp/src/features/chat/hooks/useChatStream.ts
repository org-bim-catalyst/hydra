import { useCallback, useEffect, useRef, useState } from 'react'
import * as chatsApi from '../api/chatsApi'
import type { PersistedMessage } from '../api/chatsApi'
import { generateImage, streamChat, type ChatMessage, type GenerationParameters } from '../api/aiApi'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore, type SiteBoundarySource } from '../../../store/activeSiteBoundaryStore'
import { viewerEngine } from '../../../viewer/engine/viewerEngineInstance'

/**
 * A client-side id for a streamed assistant bubble, so it has one from the moment it exists.
 * `crypto.randomUUID` is unavailable in some older WebViews and in jsdom without a polyfill.
 */
function newMessageId(): string {
  return globalThis.crypto?.randomUUID?.() ?? `local-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

const TITLE_MAX_LENGTH = 60

function toTitle(text: string): string {
  return text.length > TITLE_MAX_LENGTH ? `${text.slice(0, TITLE_MAX_LENGTH)}…` : text
}

function toChatMessages(persisted: PersistedMessage[]): ChatMessage[] {
  return persisted.map((m) => ({
    id: m.id,
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
  /** What the currently-empty assistant bubble is waiting for, e.g. "Finding the site boundary". */
  const [pendingLabel, setPendingLabel] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)
  const chatIdRef = useRef<string | null>(chatId)
  // Tracks whether the *user* has sent anything in this specific mounted view — not whether
  // initialMessages has ever been defined. A brand-new chat's first messages fetch races an
  // in-progress reply and can resolve empty; that empty result is still "defined", so gating
  // on definedness alone would permanently block a later, corrected refetch from ever being
  // applied (the "new chat blank on return" bug — see research.md Topic 6). This flag flips
  // exactly once, inside send/sendImage, and never reverses within this mount.
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

      setPendingLabel(null)
      const controller = new AbortController()
      abortRef.current = controller

      // A turn can produce more than one assistant message: the site-boundary confirmation
      // reports a second action that finishes seconds after the location did, and the server
      // sends a `messageBreak` before it rather than appending it to a bubble the user has
      // already read. The last entry is the one currently streaming.
      //
      // Each part carries its own id from the moment it exists. MessageBubble gates its replay
      // control on `message.id`, so without one the second bubble rendered with no way to play
      // it back. Only the first message's id is later replaced by the server's, via __MEMORY__ —
      // that is the one the memory trace is fetched against.
      let assistantParts = [{ id: newMessageId(), content: '' }]
      const renderParts = (parts: { id: string; content: string }[]): ChatMessage[] =>
        parts.map((part) => ({ id: part.id, role: 'assistant' as const, content: part.content }))
      let citations: ChatMessage['citations']
      let retrievalOutcome: ChatMessage['retrievalOutcome']
      let retrievalError: ChatMessage['retrievalError']
      let messageId: ChatMessage['id']
      let memoryOutcome: ChatMessage['memoryOutcome']
      try {
        const activeChatId = await ensureChatId(content)
        for await (const event of streamChat(activeChatId, history, providerId, modelId, undefined, controller.signal)) {
          if (event.type === 'content') {
            const last = assistantParts[assistantParts.length - 1]
            assistantParts = [...assistantParts.slice(0, -1), { ...last, content: last.content + event.delta }]
            if (isActiveRef.current) {
              setMessages([...history, ...renderParts(assistantParts)])
            }
          } else if (event.type === 'messageBreak') {
            // Close the current bubble and open a new one — and push it to the screen NOW rather
            // than waiting for its first character. That empty trailing bubble is what renders as
            // the thinking indicator, so the user sees the work start instead of a reply that
            // looks finished while the server spends tens of seconds on the boundary.
            if (assistantParts[assistantParts.length - 1].content !== '') {
              assistantParts = [...assistantParts, { id: newMessageId(), content: '' }]
              setPendingLabel(event.pendingLabel)
              if (isActiveRef.current) {
                setMessages([...history, ...renderParts(assistantParts)])
              }
            }
          } else if (event.type === 'retrieval') {
            // specs/016-rag-semantic-search US1 — carried on the stream's trailing event so
            // citations/the retrieval-unavailable warning render immediately, without waiting
            // for a page reload to re-fetch the persisted message (FR-037a: never silent).
            retrievalOutcome = event.outcome
            retrievalError = event.error
            citations = event.citations.map((c, index) => ({ id: `pending-${index}`, ...c }))
          } else if (event.type === 'memory') {
            // specs/018-ai-memory-system US1 — the assistant message's real persisted id only
            // exists once AppendMessageCommand has run, so this trailing event is also the
            // earliest point the "why does Lucy know this" trace becomes fetchable this session.
            messageId = event.messageId ?? undefined
            memoryOutcome = event.outcome
          } else if (event.type === 'location') {
            // specs/036-startup-geolocation US3: agent-confirmed location overrides the startup
            // geolocation in the shared store. FR-012 priority rule enforced inside setFromAgent.
            // specs/038-viewer-poi-zoom: pass viewport and locationType for altitude-accurate zoom.
            useActiveLocationStore.getState().setFromAgent(
              event.latitude,
              event.longitude,
              event.locationName,
              event.confidence,
              event.locationType,
              event.viewport,
            )

            // specs/042-site-boundary-resolution edge case: a new, unrelated site must replace
            // the previously displayed boundary, not leave it overlaid — cleared here so a
            // same-turn 'siteBoundary' event (if any) still applies cleanly, and so it's also
            // cleared correctly when boundary resolution came back Unavailable this turn (no
            // 'siteBoundary' event follows to replace it otherwise).
            if (useActiveSiteBoundaryStore.getState().siteName !== event.locationName) {
              useActiveSiteBoundaryStore.getState().clearBoundary()
            }
          } else if (event.type === 'zoom') {
            // specs/038-viewer-poi-zoom US2: explicit zoom command — only execute when an active
            // location exists (C1 fix: no zoom without a confirmed location on screen).
            if (useActiveLocationStore.getState().latitude !== null) {
              viewerEngine.zoomBy(event.direction)
            }
          } else if (event.type === 'siteBoundary') {
            // specs/042-site-boundary-resolution: resolved site boundary — replaces the
            // previously active one wholesale (a new site fully supersedes the previous one).
            useActiveSiteBoundaryStore.getState().setBoundary({
              siteName: event.siteName,
              centroid: event.centroid,
              polygon: event.polygon,
              areaSquareMeters: event.areaSquareMeters,
              confidence: event.confidence,
              confidenceLevel: event.confidenceLevel,
              source: event.source as SiteBoundarySource,
              sourceDetail: event.sourceDetail,
              alternativeCandidateNames: event.alternativeCandidateNames,
            })
          }
        }
        if (isActiveRef.current) {
          // The citations, retrieval outcome, memory outcome and persisted id all belong to the
          // reply itself — the turn's first message. Anything after it is a confirmation
          // sentence the application wrote, which none of that metadata describes.
          const [reply, ...rest] = assistantParts.filter((part, index) => part.content !== '' || index === 0)
          setMessages([
            ...history,
            { id: messageId ?? reply.id, role: 'assistant', content: reply.content, citations, retrievalOutcome, retrievalError, memoryOutcome },
            ...renderParts(rest),
          ])
        }
      } catch (err) {
        if (isActiveRef.current) {
          // FR-030: keep whatever partial content already streamed in (flagged incomplete)
          // rather than discarding it — a connection drop mid-stream shouldn't erase a
          // reply the user could already see arriving.
          // Every bubble already completed stays as it is; only the one that was still streaming
          // is flagged incomplete.
          setMessages([
            ...history,
            ...renderParts(assistantParts.slice(0, -1)),
            { ...renderParts([assistantParts[assistantParts.length - 1]])[0], isIncomplete: true },
          ])
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

  const stop = useCallback(() => abortRef.current?.abort(), [])
  const clearError = useCallback(() => setError(null), [])

  /** Resends the message content from the most recent failed send() (FR-008). No-op if nothing has failed. */
  const retry = useCallback(() => {
    if (lastAttemptedContentRef.current !== null) {
      void send(lastAttemptedContentRef.current)
    }
  }, [send])

  return { messages, isStreaming, pendingLabel, error, clearError, send, sendImage, stop, retry, providerId, modelId, setSelection }
}
