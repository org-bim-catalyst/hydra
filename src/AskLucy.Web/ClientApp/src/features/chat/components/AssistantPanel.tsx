import AddIcon from '@mui/icons-material/Add'
import { Box, Button } from '@mui/material'
import type { ReactNode } from 'react'

interface AssistantPanelProps {
  onNewChat: () => void
  children: ReactNode
}

/** FR-013: the chat conversation surface — a "New chat" action at its top (FR-009), followed
 * by chat content/controls in `children` (`ConversationView`).
 *
 * specs/025-chat-configuration-settings FR-008: `ConversationSwitcher` (browsing/reopening
 * past conversations) is relocated to the standalone Chat History tab in Settings — starting
 * a *new* conversation remains an everyday, in-workspace action per FR-009, so only that part
 * of the old switcher survives here, as a plain button rather than a popover trigger.
 *
 * SPEC-024: positioning, glassmorphism styling, and open/collapsed visibility (previously
 * owned here directly) are now the responsibility of the `CircularAction`/`FloatingPanel`
 * ancestors this renders inside (`workspaceControls.tsx`'s `chat` control) — this
 * component owns only its own internal layout. `CircularAction`'s `Collapse` (not
 * `unmountOnExit`) plus `inert` already keep this mounted-but-hidden while collapsed, so
 * an in-progress conversation is never lost, same guarantee as before. */
export function AssistantPanel({ onNewChat, children }: AssistantPanelProps) {
  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0 }}>
      <Box sx={{ px: 1, pt: 1 }}>
        <Button fullWidth variant="text" startIcon={<AddIcon />} onClick={onNewChat} sx={{ justifyContent: 'flex-start', fontWeight: 600, color: 'text.primary' }}>
          New chat
        </Button>
      </Box>
      {children}
    </Box>
  )
}
