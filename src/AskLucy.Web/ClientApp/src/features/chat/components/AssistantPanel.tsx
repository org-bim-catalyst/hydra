import { Box, useTheme } from '@mui/material'
import type { ReactNode } from 'react'
import { useAssistantPanelStore } from '../../../store/assistantPanelStore'
import { createGlassTokens } from '../../../theme/tokens/glass'
import { ConversationSwitcher } from './ConversationSwitcher'

interface AssistantPanelProps {
  selectedChatId: string | null
  onSelectChat: (id: string) => void
  onNewChat: () => void
  children: ReactNode
}

/** FR-004/FR-005/FR-006: the floating, collapsible glassmorphism panel that replaces the
 * old fixed sidebar+column layout. Hosts `ConversationSwitcher` (FR-008/FR-009) at its
 * top, followed by chat content/controls in `children` (ConversationView). Stays
 * mounted (not `unmountOnExit`) so an in-progress conversation isn't lost on collapse;
 * `inert` removes it from the tab order and AT tree while collapsed instead. */
export function AssistantPanel({
  selectedChatId,
  onSelectChat,
  onNewChat,
  children,
}: AssistantPanelProps) {
  const isOpen = useAssistantPanelStore((s) => s.isOpen)
  const theme = useTheme()
  const glass = createGlassTokens(theme.palette.mode)

  return (
    <Box
      role="region"
      aria-label="Ask Lucy assistant"
      aria-hidden={!isOpen}
      inert={!isOpen}
      sx={{
        position: 'absolute',
        top: { xs: 56, sm: 72 },
        left: { xs: 0, sm: 16 },
        // Mobile reserves a clear strip at the bottom for AssistantToggleFab (T025) so an
        // always-full-width panel never overlaps it; desktop's narrower panel doesn't need it.
        bottom: { xs: 88, sm: 16 },
        width: { xs: '100%', sm: 420 },
        maxWidth: '100%',
        zIndex: 2,
        display: 'flex',
        flexDirection: 'column',
        borderRadius: { xs: 0, sm: 4 },
        overflow: 'hidden',
        bgcolor: glass.background,
        backdropFilter: glass.backdropFilter,
        border: `1px solid ${glass.border}`,
        boxShadow: theme.shadows[8],
        transform: isOpen ? 'translateX(0)' : 'translateX(-16px)',
        opacity: isOpen ? 1 : 0,
        visibility: isOpen ? 'visible' : 'hidden',
        pointerEvents: isOpen ? 'auto' : 'none',
        // Reads theme.transitions (motion.ts, wired in theme/index.ts) — collapses to
        // instant when the user prefers reduced motion (FR-010), same as every other
        // themed transition, rather than a component-local hardcoded duration.
        transition: (t) =>
          `${t.transitions.create(['transform', 'opacity'])}, visibility ${t.transitions.duration.standard}ms step-end`,
      }}
    >
      <ConversationSwitcher
        selectedChatId={selectedChatId}
        onSelectChat={onSelectChat}
        onNewChat={onNewChat}
      />
      {children}
    </Box>
  )
}
