import {
  RiAddLine,
  RiArrowLeftLine,
  RiCollapseVerticalLine,
  RiExpandVerticalLine,
  RiVolumeMuteLine,
  RiVolumeUpLine,
} from '@remixicon/react'
import { Box, IconButton, Stack, Tooltip, Typography } from '@mui/material'
import { useEffect, useRef, type ReactNode } from 'react'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'
import { radius } from '../../../theme'
import { LucyPortrait } from '../branding/LucyPortrait'
import { ActiveLanguageFlag } from './ActiveLanguageFlag'

export interface ExpandedChatPanelProps {
  open: boolean
  onCollapse: () => void
  /** FR-014: minimal icon-only control for starting a new conversation mid-session,
   * replacing the removed `AssistantPanel` "+ New chat" text button. */
  onNewChat: () => void
  language: string
  contentId: string
  /** specs/030-composer-panel-refinements FR-007/FR-008a — whether the panel is at its
   * full-window-height state (`true`) or its default half-height state (`false`); sourced
   * from `chatPanelSizeStore` by the caller so the choice persists across reloads. */
  isFullHeight: boolean
  /** Wired to the resize/toggle button (FR-008, FR-009) — flips `isFullHeight` in the
   * caller's store. */
  onToggleHeight: () => void
  /** specs/031-voice-controls-redesign FR-011/FR-012 — relocated here from
   * `ChatComposer`'s footer so muting Lucy reads as part of her own identity/status area.
   * Behavior (including stopping in-progress speech when muted) is unchanged from before
   * the relocation, per specs/029-fix-chat-widget-bugs's merged mute+stop toggle. */
  isMuted: boolean
  onToggleMute: () => void
  headerTrailing?: ReactNode
  children: ReactNode
}

/** FR-008: the Expanded conversation panel — header (collapse control, identity/status,
 * active-language flag, minimal new-chat icon) plus `children` (`ConversationView`,
 * unchanged internally). Moves initial focus inside on open without trapping it,
 * mirroring `FloatingPanel`'s existing effect (research.md #9/#10). Sized via MUI
 * breakpoints, matching `FloatingPanel`'s own pattern (research.md #11). */
export function ExpandedChatPanel({
  open,
  onCollapse,
  onNewChat,
  language,
  contentId,
  isFullHeight,
  onToggleHeight,
  isMuted,
  onToggleMute,
  headerTrailing,
  children,
}: ExpandedChatPanelProps) {
  const contentRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const firstFocusable = contentRef.current?.querySelector<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )
    firstFocusable?.focus()
  }, [open])

  return (
    <Box
      id={contentId}
      role="region"
      aria-label="Ask Lucy assistant"
      sx={{
        display: 'flex',
        flexDirection: 'column',
        width: { xs: 'min(92vw, 380px)', sm: 400 },
        // specs/030-composer-panel-refinements FR-007, research.md Decision 3 — full-height
        // subtracts 2x ChatAssistantWidget's bottom-anchor offset so the panel (which grows
        // upward from that fixed bottom anchor) doesn't clip past the viewport's top edge.
        height: isFullHeight
          ? { xs: 'calc(100vh - 32px)', sm: 'calc(100vh - 48px)' }
          : { xs: 'min(70vh, 600px)', sm: 560 },
        overflow: 'hidden',
        borderRadius: `${radius.lg}px`,
        bgcolor: CIRCULAR_ACTION_CHROME.expandedBg,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
        transition: (theme) => theme.transitions.create(['height']),
      }}
    >
      <Stack
        direction="row"
        spacing={1}
        sx={{
          alignItems: 'center',
          px: 1,
          py: 1,
          borderBottom: '1px solid',
          borderColor: 'rgba(255,255,255,0.12)',
          color: CIRCULAR_ACTION_CHROME.icon,
        }}
      >
        <Tooltip title="Collapse">
          <IconButton
            onClick={onCollapse}
            aria-label="Collapse"
            size="small"
            sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
          >
            <RiArrowLeftLine fontSize="small" />
          </IconButton>
        </Tooltip>
        <LucyPortrait variant="toggle" alt="Ask Lucy" />
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 600, lineHeight: 1.2 }}>
            Ask Lucy
          </Typography>
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
            <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
            <Typography variant="caption" sx={{ opacity: 0.75 }}>
              Online
            </Typography>
          </Stack>
        </Box>
        {/* specs/031-voice-controls-redesign FR-011 — immediately after the name/status
            block, reading as part of Lucy's own identity area rather than a
            message-composition control. */}
        <Tooltip title={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}>
          <IconButton
            onClick={onToggleMute}
            aria-label={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}
            size="small"
            sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
          >
            {isMuted ? <RiVolumeMuteLine fontSize="small" /> : <RiVolumeUpLine fontSize="small" />}
          </IconButton>
        </Tooltip>
        <ActiveLanguageFlag language={language} />
        <Tooltip title="Start new conversation">
          <IconButton
            onClick={onNewChat}
            aria-label="Start new conversation"
            size="small"
            sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
          >
            <RiAddLine fontSize="small" />
          </IconButton>
        </Tooltip>
        {/* specs/030-composer-panel-refinements FR-008/FR-009 — immediately after the
            new-chat button per spec.md's correction, not next to the collapse arrow. */}
        <Tooltip title={isFullHeight ? 'Collapse to half height' : 'Expand to full height'}>
          <IconButton
            onClick={onToggleHeight}
            aria-label={isFullHeight ? 'Collapse to half height' : 'Expand to full height'}
            size="small"
            sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
          >
            {isFullHeight ? <RiCollapseVerticalLine fontSize="small" /> : <RiExpandVerticalLine fontSize="small" />}
          </IconButton>
        </Tooltip>
        {headerTrailing}
      </Stack>
      <Box
        ref={contentRef}
        sx={{
          flex: 1,
          minHeight: 0,
          display: 'flex',
          flexDirection: 'column',
          // specs/030-composer-panel-refinements: matches ChatPage.tsx's message-list
          // container (`bgcolor: 'background.default'`) so the composer below it — which
          // sets no bgcolor of its own and so shows this Box's color through — reads as one
          // continuous surface instead of a visible seam between two different MUI palette
          // tokens (was 'background.paper' here vs. 'background.default' on the message list).
          bgcolor: 'background.default',
          color: 'text.primary',
        }}
      >
        {children}
      </Box>
    </Box>
  )
}
