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
import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { AssistantPanel } from '../components/AssistantPanel'
import { AssistantToggleFab } from '../components/AssistantToggleFab'
import { ChatComposer } from '../components/ChatComposer'
import { LanguageSelector } from '../components/LanguageSelector'
import { MessageBubble } from '../components/MessageBubble'
import { MinimalTopBar } from '../components/MinimalTopBar'
import { ProviderModelSelector } from '../components/ProviderModelSelector'
import { ThinkingIndicator } from '../components/ThinkingIndicator'
import { useChatMessages } from '../hooks/useChats'
import { useChatStream } from '../hooks/useChatStream'
import { useVoiceOutput } from '../voice/useVoiceOutput'
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
  // on-demand read-aloud, dropping the automatic one entirely.
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

      <ChatComposer onSend={send} disabled={isStreaming || !providerId || !modelId} />
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
    </Box>
  )
}
