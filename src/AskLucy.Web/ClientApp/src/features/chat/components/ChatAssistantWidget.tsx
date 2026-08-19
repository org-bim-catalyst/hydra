import { Box } from '@mui/material'
import type { ReactNode } from 'react'

export interface ChatAssistantWidgetProps {
  children: ReactNode
}

/** specs/026-floating-chat-assistant research.md #1/#11: a fixed bottom-end anchor for
 * the chat widget, deliberately outside `WorkspaceOverlay`'s `FloatingToolbar`/
 * `CircularAction` system (which stays unchanged for the other six Studio controls) —
 * this widget's Collapsed/Expanded shapes don't fit that system's single-icon-circle
 * assumption. Purely positional: `ConversationView` (rendered as `children`) owns the
 * actual Collapsed/Expanded branching, `workspaceOverlayStore` wiring, and keyboard/ARIA
 * contract (research.md #9) directly, since it already holds the live voice/streaming
 * state both visual states need, and must remain directly renderable/testable in
 * isolation — many existing tests render it standalone, outside any wrapper. */
export function ChatAssistantWidget({ children }: ChatAssistantWidgetProps) {
  return (
    <Box
      sx={{
        position: 'absolute',
        bottom: { xs: 16, sm: 24 },
        right: { xs: 16, sm: 24 },
        zIndex: 3,
        pointerEvents: 'auto',
      }}
    >
      {children}
    </Box>
  )
}
