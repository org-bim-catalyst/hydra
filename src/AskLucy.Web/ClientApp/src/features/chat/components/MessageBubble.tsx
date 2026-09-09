import {
  RiAttachment2,
  RiCheckLine,
  RiFileCopyLine,
  RiPlayFill,
  RiStopFill,
} from '@remixicon/react'
import { Alert, Box, Chip, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material'
import 'katex/dist/katex.min.css'
import { useCallback, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import rehypeKatex from 'rehype-katex'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import type { ChatMessage, SuggestedAction } from '../api/aiApi'
import { CitationBadge } from '../../retrieval/components/CitationBadge'
import { MemoryTraceIndicator } from '../../memory/components/MemoryTraceIndicator'
import { SuggestedActionCard } from './SuggestedActionCard'
import { codeFontFamily } from '../../../theme/tokens/typography'
import { radius } from '../../../theme'

/** Renders Markdown + KaTeX math (FR-007), preserved from the legacy chat UI. `chatId` is only needed for the memory trace indicator (specs/018-ai-memory-system US1) — omit it and the indicator simply never renders (e.g. in isolated component tests).
 *
 * specs/039-composer-interaction-states-redesign FR-020–FR-025 (User Story 5) — the
 * replay/stop control. `showStopIcon`/`isReplayDisabled` are computed by the caller
 * (`ChatPage.tsx`, contracts/reply-playback-control.md), not derived here, since only the
 * caller knows which single message (if any) is currently the target of the shared
 * `useVoiceOutput` playback channel.
 *
 * specs/046-reply-action-bar FR-001–FR-009 — Replay/Stop (relocated) and Copy render together
 * in a left-aligned action row below the bubble, ChatGPT/Claude-style, instead of floating
 * inside it. */
export function MessageBubble({
  message,
  chatId,
  showStopIcon,
  isReplayDisabled,
  onReplay,
  onStopReplay,
  isLiveOffer,
  onSelectAction,
  isSubmittingAction,
  actionError,
}: {
  message: ChatMessage
  chatId?: string | null
  /** True only when this message is the one playing AND that playback was user-initiated
   * (drives the interactive Stop control, FR-022/FR-024). False for a message currently
   * auto-speaking for the first time, even though it "is playing" (F1). Omit entirely (no
   * replay control renders) when the caller doesn't wire replay at all — e.g. isolated
   * tests that only care about message content. */
  showStopIcon?: boolean
  isReplayDisabled?: boolean
  onReplay?: (message: ChatMessage) => void
  onStopReplay?: () => void
  /**
   * specs/045-conversational-agent-runtime US3, data-model.md §2 — "the live offer is the
   * newest assistant message in the chat with non-null SuggestedActionsJson that no later user
   * message has already answered." Only the caller (`ChatPage.tsx`) can know that across the
   * whole message list; omit it (or pass false) and an offer on this message renders inert.
   */
  isLiveOffer?: boolean
  onSelectAction?: (offeredByMessageId: string, action: SuggestedAction) => Promise<void>
  isSubmittingAction?: boolean
  actionError?: string | null
}) {
  const isUser = message.role === 'user'
  const hasAttachments = (message.attachments?.length ?? 0) > 0
  const hasCitations = (message.citations?.length ?? 0) > 0
  const hasAttribution = !isUser && Boolean(message.provider || message.model)
  // research.md Decision 7 — only a completed assistant reply (a stable id, not the
  // still-streaming placeholder) gets a replay control; also requires the caller to have
  // actually wired the replay handlers (onReplay undefined means "not offered here").
  const showReplayControl = !isUser && Boolean(message.id) && Boolean(onReplay)
  // specs/046-reply-action-bar FR-001/FR-007/FR-009 — the action row (Copy, plus Replay when
  // offered) renders for any completed assistant reply, independent of whether replay itself
  // is wired up by the caller.
  const showActionRow = !isUser && Boolean(message.id)

  const [copyStatus, setCopyStatus] = useState<'idle' | 'success' | 'error'>('idle')
  const handleCopy = useCallback(async () => {
    try {
      await navigator.clipboard.writeText(message.content)
      setCopyStatus('success')
    } catch {
      setCopyStatus('error')
    } finally {
      window.setTimeout(() => setCopyStatus('idle'), 2000)
    }
  }, [message.content])

  return (
    <Box sx={{ display: 'flex', justifyContent: isUser ? 'flex-end' : 'flex-start', mb: 2 }}>
      {/* specs/046-reply-action-bar FR-004/FR-005 — a column wrapper so the action row stacks
          below the bubble (not beside it in this flex row), while the outer Box above still
          controls left/right alignment of the whole bubble+row unit. */}
      <Box sx={{ display: 'flex', flexDirection: 'column', maxWidth: '75%' }}>
        <Paper
          elevation={isUser ? 0 : 1}
          sx={{
            px: 2.25,
            py: 1.25,
            borderRadius: `${radius.lg}px`,
            // The "tail" corner reads as pointing toward its sender, matching the
            // premium-AI-chat idiom (ChatGPT/Claude) rather than a uniform pill.
            ...(isUser && { borderBottomRightRadius: radius.xs }),
            ...(!isUser && { borderBottomLeftRadius: radius.xs }),
            bgcolor: isUser ? 'primary.main' : 'background.paper',
            color: isUser ? 'primary.contrastText' : 'text.primary',
            border: isUser ? 'none' : '1px solid',
            borderColor: 'divider',
            '& p:first-of-type': { mt: 0 },
            '& p:last-of-type': { mb: 0 },
            '& img': { maxWidth: '100%', borderRadius: `${radius.sm}px` },
            '& code': {
              fontFamily: codeFontFamily,
              fontSize: '0.875em',
              bgcolor: isUser ? 'rgba(247, 246, 242, 0.16)' : 'action.hover',
              borderRadius: `${radius.xs}px`,
              px: 0.5,
              py: 0.15,
            },
            '& pre': {
              fontFamily: codeFontFamily,
              bgcolor: isUser ? 'rgba(247, 246, 242, 0.16)' : 'action.hover',
              borderRadius: `${radius.sm}px`,
              p: 1.5,
              overflowX: 'auto',
            },
            '& pre code': { bgcolor: 'transparent', p: 0 },
          }}
        >
          <Typography component="div" variant="body1">
            <ReactMarkdown remarkPlugins={[remarkGfm, remarkMath]} rehypePlugins={[rehypeKatex]}>
              {message.content}
            </ReactMarkdown>
          </Typography>

          {(hasAttachments || hasCitations) && (
            <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: 'wrap' }}>
              {message.attachments?.map((a) => (
                <Chip
                  key={a.id}
                  size="small"
                  icon={<RiAttachment2 size={18} />}
                  label={a.fileName}
                  component="a"
                  href={a.accessLocation}
                  target="_blank"
                  rel="noopener noreferrer"
                  clickable
                />
              ))}
              {message.citations?.map((c) => (
                <CitationBadge key={c.id} citation={c} />
              ))}
            </Stack>
          )}

          {/* specs/016-rag-semantic-search US1 (research.md Decision 8) — a non-silent, visible
            indicator for a degraded response: NoRelevantContent means the message answered
            without grounding because nothing relevant was found; Unavailable means retrieval
            itself failed but the response still generated (FR-037a — never blocked, never silent). */}
          {message.retrievalOutcome === 'NoRelevantContent' && (
            <Alert severity="info" variant="outlined" sx={{ mt: 1, py: 0 }}>
              No relevant content was found in the attached knowledge base(s) — this response isn't
              grounded in your documents.
            </Alert>
          )}
          {message.retrievalOutcome === 'Unavailable' && (
            <Alert severity="warning" variant="outlined" sx={{ mt: 1, py: 0 }}>
              {message.retrievalError ??
                "Knowledge base retrieval was temporarily unavailable — this response isn't grounded in your documents."}
            </Alert>
          )}

          {/* specs/018-ai-memory-system US1 (FR-014) — only rendered (and only then does its own
            query hook mount) when this response actually used a remembered fact/preference; a
            subtle, non-intrusive affordance the user opens on demand, never shown unprompted. */}
          {message.memoryOutcome === 'Found' && chatId && message.id && (
            <MemoryTraceIndicator chatId={chatId} messageId={message.id} />
          )}

          {/* specs/045-conversational-agent-runtime FR-021/FR-026/FR-029 — rendered whenever this
            message carries an offer; only the newest unanswered one (isLiveOffer) is interactive. */}
          {message.suggestedActions && message.suggestedActions.length > 0 && message.id && (
            <SuggestedActionCard
              question={message.question ?? 'What would you like to do next?'}
              actions={message.suggestedActions}
              isLive={Boolean(isLiveOffer)}
              isSubmitting={Boolean(isSubmittingAction)}
              error={actionError ?? null}
              onSelect={(action) => {
                const offeredByMessageId = message.id
                return offeredByMessageId
                  ? (onSelectAction?.(offeredByMessageId, action) ?? Promise.resolve())
                  : Promise.resolve()
              }}
            />
          )}

          {/* specs/005-multi-provider-ai-engine FR-011: attribution is a snapshot of what
            actually produced this message, independent of the conversation's current
            provider/model selection. */}
          {(hasAttribution || message.isIncomplete) && (
            <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: 'wrap' }}>
              {hasAttribution && (
                <Chip
                  size="small"
                  variant="outlined"
                  label={[message.provider, message.model].filter(Boolean).join(' · ')}
                />
              )}
              {message.isIncomplete && (
                <Chip
                  size="small"
                  color="warning"
                  variant="outlined"
                  label="Incomplete — connection dropped"
                />
              )}
            </Stack>
          )}
        </Paper>

        {/* specs/046-reply-action-bar FR-004/FR-005 — a normal-flow row beneath the bubble,
          left-aligned, matching the ChatGPT/Claude action-row convention (replaces the old
          absolutely-positioned in-bubble control). FR-020–FR-025 (Figure 8, specs/039):
          RiPlayFill (disabled per isReplayDisabled) when this reply isn't the one currently
          user-replaying; RiStopFill (always enabled per FR-024) when it is. Never both
          handlers on the same click. */}
        {showActionRow && (
          <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, justifyContent: 'flex-start' }}>
            {showReplayControl && (
              <Tooltip title={showStopIcon ? 'Stop' : 'Replay'}>
                <IconButton
                  size="small"
                  disabled={!showStopIcon && isReplayDisabled}
                  onClick={() => (showStopIcon ? onStopReplay?.() : onReplay?.(message))}
                  aria-label={showStopIcon ? 'Stop' : 'Replay'}
                >
                  {showStopIcon ? <RiStopFill size={16} /> : <RiPlayFill size={16} />}
                </IconButton>
              </Tooltip>
            )}
            {/* specs/046-reply-action-bar FR-001–FR-003 — copies message.content verbatim; the
              catch branch is what produces the visible failure state, never a discard
              (CLAUDE.md Error Handling — no silent failures). */}
            <Tooltip
              title={
                copyStatus === 'success'
                  ? 'Copied'
                  : copyStatus === 'error'
                    ? 'Copy failed'
                    : 'Copy'
              }
            >
              <IconButton
                size="small"
                onClick={handleCopy}
                aria-label={
                  copyStatus === 'success'
                    ? 'Copied'
                    : copyStatus === 'error'
                      ? 'Copy failed'
                      : 'Copy'
                }
              >
                {copyStatus === 'success' ? (
                  <RiCheckLine size={16} />
                ) : (
                  <RiFileCopyLine size={16} />
                )}
              </IconButton>
            </Tooltip>
          </Stack>
        )}
      </Box>
    </Box>
  )
}
