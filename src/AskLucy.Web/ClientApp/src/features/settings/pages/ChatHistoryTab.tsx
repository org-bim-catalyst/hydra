import { Box } from '@mui/material'
import { useNavigate } from 'react-router'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
import { ConversationList } from '../../chat/components/ChatSidebar'

/**
 * specs/025-chat-configuration-settings FR-006/FR-007 — a standalone Settings tab, unrelated
 * to and not nested inside Chat Configuration (Clarifications Q2): relocates the existing
 * conversation list unchanged. Selecting or starting a conversation updates the shared
 * `activeConversationStore` (research.md Decision 1) and returns the user to the workspace.
 */
export function ChatHistoryTab() {
  const navigate = useNavigate()
  const activeChatId = useActiveConversationStore((s) => s.activeChatId)
  const setActiveChatId = useActiveConversationStore((s) => s.setActiveChatId)

  const handleSelectChat = (id: string) => {
    setActiveChatId(id)
    navigate('/studio')
  }

  const handleNewChat = () => {
    setActiveChatId(null)
    navigate('/studio')
  }

  return (
    <Box sx={{ height: 560, maxWidth: 480 }}>
      <ConversationList
        selectedChatId={activeChatId}
        onSelectChat={handleSelectChat}
        onNewChat={handleNewChat}
      />
    </Box>
  )
}
