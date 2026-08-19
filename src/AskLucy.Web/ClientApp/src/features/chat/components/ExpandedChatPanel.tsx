import { RiAddLine, RiArrowLeftLine } from '@remixicon/react'
import { Box, IconButton, Stack, Typography } from '@mui/material'
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
        height: { xs: 'min(70vh, 600px)', sm: 560 },
        overflow: 'hidden',
        borderRadius: `${radius.lg}px`,
        bgcolor: CIRCULAR_ACTION_CHROME.expandedBg,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
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
        <IconButton
          onClick={onCollapse}
          aria-label="Collapse"
          size="small"
          sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
        >
          <RiArrowLeftLine fontSize="small" />
        </IconButton>
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
        <ActiveLanguageFlag language={language} />
        <IconButton
          onClick={onNewChat}
          aria-label="Start new conversation"
          size="small"
          sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
        >
          <RiAddLine fontSize="small" />
        </IconButton>
        {headerTrailing}
      </Stack>
      <Box
        ref={contentRef}
        sx={{
          flex: 1,
          minHeight: 0,
          display: 'flex',
          flexDirection: 'column',
          bgcolor: 'background.paper',
          color: 'text.primary',
        }}
      >
        {children}
      </Box>
    </Box>
  )
}
