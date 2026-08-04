import ImageIcon from '@mui/icons-material/Image'
import TranslateIcon from '@mui/icons-material/Translate'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  IconButton,
  Snackbar,
  Toolbar,
  Typography,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useVirtualizer } from '@tanstack/react-virtual'
import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { AssistantPanel } from '../components/AssistantPanel'
import { AssistantToggleFab } from '../components/AssistantToggleFab'
import { ChatComposer } from '../components/ChatComposer'
import { LanguageSelector } from '../components/LanguageSelector'
import { MessageBubble } from '../components/MessageBubble'
import { MinimalTopBar } from '../components/MinimalTopBar'
import { ProviderModelSelector } from '../components/ProviderModelSelector'
import { ThinkingIndicator } from '../components/ThinkingIndicator'
import { VoiceControlBar } from '../components/VoiceControlBar'
import { useChatMessages } from '../hooks/useChats'
import { useChatStream } from '../hooks/useChatStream'
import { useSpeechRecognition } from '../voice/useSpeechRecognition'
import { useVoiceOutput } from '../voice/useVoiceOutput'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import { useAssistantPanelStore } from '../../../store/assistantPanelStore'

const SceneBackground = lazy(() =>
  import('../scene/SceneBackground').then((m) => ({ default: m.SceneBackground })),
)

/**
 * Owns which chat is selected (2026-07-28 ChatGPT-style history decision). `ConversationView`
 * below is remounted (via `key`) only on an *explicit* navigation — picking a different
 * sidebar chat, or starting a new one — never when a chat is auto-created mid-send (see
 * `handleChatCreated`), so an in-flight stream is never interrupted by its own id arriving.
 */
export function ChatPage() {
  const [selectedChatId, setSelectedChatId] = useState<string | null>(null)
  const [viewKey, setViewKey] = useState(0)
  const [language, setLanguage] = useState('en')
  const queryClient = useQueryClient()
  // Lifted above ConversationView so the same isSpeaking/intensity state drives both the
  // actual voice playback (triggered from ConversationView) and the sphere reacting to it
  // (SceneBackground, a sibling) — a separate hook instance per component wouldn't share state.
  const tts = useVoiceOutput()

  // FR-011/SC-004: restores a returning user's mute/input-mode preference without requiring
  // a detour through Settings first (research.md Decision 9 — VoiceTab already hydrates on
  // its own mount, but a user who never opens Settings needs this too). Mounted once here,
  // not in ConversationView, since ConversationView remounts on every chat switch.
  const hydrateVoicePreferences = useVoicePreferencesStore((s) => s.hydrateFromServer)
  useEffect(() => {
    void hydrateVoicePreferences()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const handleSelectChat = (id: string) => {
    setSelectedChatId(id)
    setViewKey((k) => k + 1)
  }

  const handleNewChat = () => {
    setSelectedChatId(null)
    setViewKey((k) => k + 1)
  }

  const handleChatCreated = (id: string) => {
    setSelectedChatId(id)
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  return (
    <Box sx={{ position: 'relative', height: '100dvh', width: '100%', overflow: 'hidden' }}>
      <Suspense
        fallback={
          <Box sx={{ position: 'absolute', inset: 0, zIndex: 0, bgcolor: 'background.default' }} />
        }
      >
        <SceneBackground getReactiveIntensity={tts.getIntensity} />
      </Suspense>
      <MinimalTopBar />
      <AssistantPanel
        selectedChatId={selectedChatId}
        onSelectChat={handleSelectChat}
        onNewChat={handleNewChat}
      >
        <ConversationView
          key={viewKey}
          chatId={selectedChatId}
          language={language}
          onLanguageChange={setLanguage}
          onChatCreated={handleChatCreated}
          tts={tts}
        />
      </AssistantPanel>
      <AssistantToggleFab />
    </Box>
  )
}

interface ConversationViewProps {
  chatId: string | null
  language: string
  onLanguageChange: (language: string) => void
  onChatCreated: (id: string) => void
  tts: ReturnType<typeof useVoiceOutput>
}

export function ConversationView({
  chatId,
  language,
  onLanguageChange,
  onChatCreated,
  tts,
}: ConversationViewProps) {
  const {
    data,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    isPending: isMessagesPending,
    isError: isMessagesError,
    refetch: refetchMessages,
  } = useChatMessages(chatId)

  // FR-024: a long conversation's full history is loaded incrementally (background-fetched
  // page by page here) rather than all at once, then rendered with only the visible portion
  // mounted (the virtualizer below) — the two together keep scrolling smooth regardless of
  // conversation length.
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage) {
      void fetchNextPage()
    }
  }, [hasNextPage, isFetchingNextPage, fetchNextPage])

  const persistedMessages = useMemo(() => data?.pages.flatMap((page) => page.items), [data])

  const {
    messages,
    isStreaming,
    error,
    clearError,
    send,
    sendImage,
    sendTranslation,
    retry,
    providerId,
    modelId,
    setSelection,
  } = useChatStream(chatId, persistedMessages, onChatCreated)
  const scrollRef = useRef<HTMLDivElement>(null)
  const listParentRef = useRef<HTMLDivElement>(null)

  const virtualizer = useVirtualizer({
    count: messages.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: () => 96,
    overscan: 8,
  })

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Restores the legacy app's behavior of speaking every AI reply aloud as soon as it
  // finishes streaming (FR-006) — the React migration had only kept the Translate button's
  // on-demand read-aloud, dropping the automatic one entirely. `tts.speak()` itself no-ops
  // while muted (useVoiceOutput.ts, SPEC-013 Decision 3), so this effect needs no isMuted
  // check of its own.
  const isPanelOpen = useAssistantPanelStore((s) => s.isOpen)
  const markUnread = useAssistantPanelStore((s) => s.markUnread)
  const wasStreamingRef = useRef(false)
  useEffect(() => {
    if (wasStreamingRef.current && !isStreaming) {
      const last = messages[messages.length - 1]
      if (last?.role === 'assistant' && last.content) {
        tts.speak(last.content, language)
        // FR-016: the toggle needs to indicate new activity when the panel is collapsed.
        if (!isPanelOpen) markUnread()
      }
    }
    wasStreamingRef.current = isStreaming
  }, [isStreaming, messages, language, tts, isPanelOpen, markUnread])

  // SPEC-013 US1 (FR-001/FR-003): keeps the extended useVoiceOutput's real-time mute gate
  // in sync with the persisted preference — store is the source of truth (VoiceControlBar
  // and Settings' VoiceTab both write through it), tts.isMuted is only its live effect.
  const isMutedPreference = useVoicePreferencesStore((s) => s.isMuted)
  const updateVoicePreference = useVoicePreferencesStore((s) => s.update)
  // FR-012/constitution §2.VIII: a rejected `update({ isMuted })` (e.g. offline, 500) rolls
  // back to the last-known-good state inside the store itself (voicePreferencesStore.ts) —
  // this just makes that failure visible instead of leaving it store-internal only.
  const voicePreferenceError = useVoicePreferencesStore((s) => s.error)
  const clearVoicePreferenceError = useVoicePreferencesStore((s) => s.clearError)
  useEffect(() => {
    tts.setMuted(isMutedPreference)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isMutedPreference])

  const conversationMode = useVoicePreferencesStore((s) => s.conversationMode)
  const updateConversationMode = useVoicePreferencesStore((s) => s.update)

  // SPEC-013 US2: the composer's text field is lifted here (rather than owned internally by
  // ChatComposer) so a Push-to-Talk transcript can fill it directly (research.md Decision 4),
  // the same way a Continuous-mode transcript calls `send()` directly below.
  const [composerText, setComposerText] = useState('')
  const handleSend = () => {
    if (!composerText.trim()) return
    send(composerText.trim())
    setComposerText('')
  }

  // `useSpeechRecognition` attaches its WebSocket 'message' listener exactly once per
  // connection (inside `start()`), closing over whatever `onFinalTranscript` instance was
  // current in *that* render — a plain inline arrow function here would freeze `providerId`/
  // `modelId`/`isStreaming`/`conversationMode` at their values from the render Continuous
  // mode happened to auto-start listening in (often before the provider/model catalog has
  // finished loading), and never see later updates for the rest of that connection's
  // lifetime. Routing through a ref that's refreshed every render (and calling through a
  // stable wrapper) keeps the handler reading current values on every call instead.
  const handleFinalTranscriptRef = useRef<(transcript: string) => void>(() => {})
  useEffect(() => {
    handleFinalTranscriptRef.current = (transcript: string) => {
      if (!transcript.trim()) return
      // Continuous mode can start listening (e.g. on mount, if the mode was already
      // Continuous from a prior session) before the provider/model catalog has finished
      // loading/auto-selecting — auto-sending in that window would hit the same
      // "Choose an AI provider and model" guard useChatStream's send() already enforces for
      // the manual Send button, but silently discard the user's spoken words instead of just
      // rejecting a typed one. Fall back to filling the composer instead of sending, so a
      // transcript is never lost — the user can send manually once ready, same as
      // Push-to-Talk's normal behavior.
      if (conversationMode === 'Continuous' && providerId && modelId && !isStreaming) {
        send(transcript.trim())
      } else {
        setComposerText((prev) => `${prev} ${transcript}`.trim())
      }
    }
  })
  const handleFinalTranscript = useCallback(
    (transcript: string) => handleFinalTranscriptRef.current(transcript),
    [],
  )

  // SPEC-013 US2 (research.md Decision 1/4): a single `useSpeechRecognition` instance, owned
  // here (mirroring `tts`'s existing lifted-hook convention), shared by `ChatComposer` (mic
  // control + transcript target) and `VoiceControlBar` (status display) below — not
  // `useConversationAudio`, which would coincidentally also speak the reply and regress the
  // "every reply is spoken, typed or voice" behavior the mute effect above preserves.
  const recognition = useSpeechRecognition({
    language,
    mode: conversationMode === 'Continuous' ? 'continuous' : 'push-to-talk',
    onPartialTranscript: () => {},
    onFinalTranscript: handleFinalTranscript,
  })

  // FR-006: Continuous mode has no per-utterance activation — selecting it starts listening
  // immediately (and keeps listening across utterances); switching away stops it. Push-to-Talk
  // capture itself is started/stopped by ChatComposer's mic control, not this effect.
  //
  // Also pauses Continuous listening while Lucy is speaking (`tts.isSpeaking`) and resumes it
  // once she finishes — without this, the always-on mic hears her own TTS audio through the
  // speakers and transcribes it as if the user said it, producing replies to nothing anyone
  // actually said. `cancel()` (discard), not `stop()` (commit), since by the time a reply is
  // audible the user's real utterance was already finalized well before generation/TTS synthesis
  // finished (the silence-commit window is 800ms; a full reply takes far longer) — anything
  // still accumulating when playback starts is not a genuine new utterance to process. This
  // only applies to Continuous mode: a Push-to-Talk hold is an explicit user gesture (e.g. a
  // deliberate barge-in) and is never force-stopped just because Lucy is talking.
  useEffect(() => {
    if (conversationMode === 'Continuous') {
      if (tts.isSpeaking) {
        if (recognition.isListening) recognition.cancel()
      } else if (!recognition.isListening) {
        void recognition.start()
      }
    } else if (recognition.isListening) {
      recognition.stop()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [conversationMode, tts.isSpeaking])

  // FR-007/Clarification Q4 (research.md Decision 6): blocks switching away from Push-to-Talk
  // while a capture (hold or toggle) is actively in progress, until it's released/stopped.
  const isModeSwitchBlocked = conversationMode === 'PushToTalk' && recognition.isListening
  const handleToggleMode = () => {
    if (isModeSwitchBlocked) return
    void updateConversationMode({
      conversationMode: conversationMode === 'PushToTalk' ? 'Continuous' : 'PushToTalk',
    })
  }

  const handleTranslateLast = async () => {
    const lastAssistant = [...messages].reverse().find((m) => m.role === 'assistant')
    if (!lastAssistant) return
    const plain = await sendTranslation(lastAssistant.content, language)
    tts.speak(plain, language)
  }

  const handleGenerateImage = async () => {
    const prompt = window.prompt('Describe the image to generate:')
    if (!prompt) return
    await sendImage(prompt)
  }

  return (
    <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      {/* FR-007/FR-015: chat-specific controls only — brand, theme, and account access
          live in MinimalTopBar, outside this panel. */}
      <Toolbar variant="dense" sx={{ justifyContent: 'flex-end', gap: 0.5 }}>
        <ProviderModelSelector providerId={providerId} modelId={modelId} onSelect={setSelection} />
        <LanguageSelector value={language} onChange={onLanguageChange} />
        <IconButton onClick={handleTranslateLast} aria-label="Translate last response">
          <TranslateIcon />
        </IconButton>
        <IconButton onClick={handleGenerateImage} aria-label="Generate image">
          <ImageIcon />
        </IconButton>
      </Toolbar>

      <Box
        ref={listParentRef}
        sx={{ flex: 1, overflow: 'auto', p: 2, bgcolor: 'background.default' }}
      >
        <Box sx={{ maxWidth: 800, mx: 'auto' }}>
          {chatId === null ? (
            // FR-001: this placeholder is reserved for "no conversation selected" only — it
            // must never be the fallback for a selected conversation that's loading/erroring.
            <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', mt: 8 }}>
              Start a conversation with Ask Lucy.
            </Typography>
          ) : isMessagesPending ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
              <CircularProgress
                role="status"
                aria-live="polite"
                aria-label="Loading conversation…"
              />
            </Box>
          ) : isMessagesError ? (
            <Box role="alert" sx={{ textAlign: 'center', mt: 8 }}>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                Failed to load this conversation. Please try again.
              </Typography>
              <Button variant="outlined" onClick={() => void refetchMessages()}>
                Retry
              </Button>
            </Box>
          ) : (
            <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
              {virtualizer.getVirtualItems().map((virtualItem) => {
                const message = messages[virtualItem.index]
                // FR-006/FR-007: the in-flight assistant placeholder (empty content while
                // streaming) renders as the thinking indicator instead of an empty bubble.
                const isThinking =
                  isStreaming && message.role === 'assistant' && message.content === ''
                return (
                  <Box
                    key={virtualItem.key}
                    data-index={virtualItem.index}
                    ref={virtualizer.measureElement}
                    sx={{
                      position: 'absolute',
                      top: 0,
                      left: 0,
                      width: '100%',
                      transform: `translateY(${virtualItem.start}px)`,
                    }}
                  >
                    {isThinking ? <ThinkingIndicator /> : <MessageBubble message={message} />}
                  </Box>
                )
              })}
            </Box>
          )}
          <div ref={scrollRef} />
        </Box>
      </Box>

      <VoiceControlBar
        isAvailable={tts.isSupported}
        isListening={recognition.isListening}
        isSpeaking={tts.isSpeaking}
        isMuted={tts.isMuted}
        conversationMode={conversationMode}
        errorMessage={recognition.error}
        permissionState={recognition.permissionState}
        onStart={() => void recognition.start()}
        onStop={recognition.stop}
        onCancel={recognition.cancel}
        onStopSpeaking={tts.stop}
        onToggleMode={handleToggleMode}
        onToggleMute={() => updateVoicePreference({ isMuted: !isMutedPreference })}
        onClearError={recognition.clearError}
      />
      <ChatComposer
        value={composerText}
        onChange={setComposerText}
        onSend={handleSend}
        disabled={isStreaming || !providerId || !modelId}
        conversationMode={conversationMode}
        isListening={recognition.isListening}
        permissionState={recognition.permissionState}
        captureError={recognition.error}
        onStartCapture={() => void recognition.start()}
        onStopCapture={recognition.stop}
        onCancelCapture={recognition.cancel}
        onClearCaptureError={recognition.clearError}
      />
      <Snackbar open={Boolean(error)} autoHideDuration={5000} onClose={clearError}>
        <Alert
          severity="error"
          variant="filled"
          onClose={clearError}
          action={
            <Button color="inherit" size="small" onClick={retry}>
              Retry
            </Button>
          }
        >
          {error}
        </Alert>
      </Snackbar>
      <Snackbar open={Boolean(tts.error)} autoHideDuration={5000} onClose={tts.clearError}>
        <Alert severity="error" variant="filled" onClose={tts.clearError}>
          {tts.error}
        </Alert>
      </Snackbar>
      <Snackbar
        open={Boolean(voicePreferenceError)}
        autoHideDuration={5000}
        onClose={clearVoicePreferenceError}
      >
        <Alert severity="error" variant="filled" onClose={clearVoicePreferenceError}>
          {voicePreferenceError}
        </Alert>
      </Snackbar>
    </Box>
  )
}
