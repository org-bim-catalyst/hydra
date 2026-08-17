import { Box } from '@mui/material'
import type { ReactNode } from 'react'
import { ConversationSwitcher } from './ConversationSwitcher'

interface AssistantPanelProps {
  selectedChatId: string | null
  onSelectChat: (id: string) => void
  onNewChat: () => void
  children: ReactNode
}

/** FR-013: the chat conversation surface — hosts `ConversationSwitcher` (FR-008/FR-009)
 * at its top, followed by chat content/controls in `children` (`ConversationView`).
 *
 * SPEC-024: positioning, glassmorphism styling, and open/collapsed visibility (previously
 * owned here directly) are now the responsibility of the `CircularAction`/`FloatingPanel`
 * ancestors this renders inside (`workspaceControls.tsx`'s `chat` control) — this
 * component owns only its own internal layout. `CircularAction`'s `Collapse` (not
 * `unmountOnExit`) plus `inert` already keep this mounted-but-hidden while collapsed, so
 * an in-progress conversation is never lost, same guarantee as before. */
export function AssistantPanel({ selectedChatId, onSelectChat, onNewChat, children }: AssistantPanelProps) {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      <ConversationSwitcher
        selectedChatId={selectedChatId}
        onSelectChat={onSelectChat}
        onNewChat={onNewChat}
      />
      {children}
    </Box>
  )
}
