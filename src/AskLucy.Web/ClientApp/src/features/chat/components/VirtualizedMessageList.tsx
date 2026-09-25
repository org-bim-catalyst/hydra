import { Box } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import type { ChatMessage, SuggestedAction } from '../api/aiApi'
import type { VoiceControlsProps } from './CollapsedVoiceControls'
import { MessageBubble } from './MessageBubble'
import { ThinkingIndicator } from './ThinkingIndicator'

interface VirtualizedMessageListProps {
  /** The scroll container, owned by the parent. A DOM element rather than a ref: see below. */
  scrollElement: HTMLDivElement | null
  messages: ChatMessage[]
  chatId: string | null
  isStreaming: boolean
  pendingLabel: string | null
  playingMessageId: string | null
  isManualReplay: boolean
  voiceControlsProps: VoiceControlsProps
  handleReplay: (message: ChatMessage) => void
  handleStopReplay: () => void
  liveOfferMessageId: string | null
  selectAction: (offeredByMessageId: string, action: SuggestedAction) => Promise<void>
  /** specs/068 US2 - passed straight through to every bubble; each one decides for itself whether
   * its own recorded outcome earns a retry control. */
  retryAction: (failedMessageId: string) => Promise<void>
  isSelectingAction: boolean
  actionError: string | null
}

/**
 * Owns `useVirtualizer()` and everything downstream of it, isolated from `ChatPage` so that
 * page keeps its React Compiler memoization. TanStack Virtual's hook returns functions that
 * cannot be memoized safely, which makes the compiler skip memoizing whichever component calls
 * it (react-hooks/incompatible-library) — confining that to this small, presentation-only leaf
 * means the much larger page around it is unaffected. The scroll container is still owned by the
 * parent, since it also wraps the empty/loading/error branches next to this one — only the
 * virtualized "loaded" branch's rendering moves here.
 *
 * The parent passes that container as an element held in state, not as a ref. React runs this
 * component's layout effects before it attaches refs on the ancestor that owns the container, so
 * on a fresh mount (e.g. reopening the chat panel after collapsing it) a ref still read `null`
 * when the virtualizer looked for its scroll element. The virtualizer then rendered no rows at
 * all, and the bubbles only reappeared on some unrelated re-render, such as toggling the panel's
 * height. With the element in the parent's state, its attachment is itself a re-render.
 */
export function VirtualizedMessageList({
  scrollElement,
  messages,
  chatId,
  isStreaming,
  pendingLabel,
  playingMessageId,
  isManualReplay,
  voiceControlsProps,
  handleReplay,
  handleStopReplay,
  liveOfferMessageId,
  selectAction,
  retryAction,
  isSelectingAction,
  actionError,
}: VirtualizedMessageListProps) {
  // TanStack Virtual's useVirtualizer() returns functions React Compiler cannot memoize
  // safely; isolating it to this leaf (rather than suppressing globally) is the point of
  // this extraction, so the notice here is expected and permanently accepted.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: messages.length,
    getScrollElement: () => scrollElement,
    estimateSize: () => 96,
    overscan: 8,
  })

  return (
    <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
      {virtualizer.getVirtualItems().map((virtualItem) => {
        const message = messages[virtualItem.index]
        // FR-006/FR-007: the in-flight assistant placeholder (empty content while
        // streaming) renders as the thinking indicator instead of an empty bubble.
        const isThinking = isStreaming && message.role === 'assistant' && message.content === ''
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
            {isThinking ? (
              <ThinkingIndicator label={pendingLabel ?? undefined} />
            ) : (
              <MessageBubble
                message={message}
                chatId={chatId}
                // All replay buttons are disabled while Lucy is actively speaking — whether
                // TTS (isSpeaking) or continuous mode (isListening covers every non-Idle
                // state including AiSpeaking). The current manual-replay message shows a
                // Stop button via showStopIcon (always enabled, FR-024) and is therefore
                // never reached by isReplayDisabled at all.
                showStopIcon={message.id === playingMessageId && isManualReplay}
                isReplayDisabled={
                  !message.id ||
                  voiceControlsProps.isListening ||
                  voiceControlsProps.isSpeaking ||
                  (message.id === playingMessageId && !isManualReplay)
                }
                onReplay={handleReplay}
                onStopReplay={handleStopReplay}
                isLiveOffer={Boolean(message.id) && message.id === liveOfferMessageId}
                onSelectAction={selectAction}
                onRetry={retryAction}
                isSubmittingAction={isSelectingAction}
                actionError={actionError}
              />
            )}
          </Box>
        )
      })}
    </Box>
  )
}
