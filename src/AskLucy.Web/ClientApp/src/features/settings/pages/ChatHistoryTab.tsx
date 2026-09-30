import { Box } from '@mui/material'
import { useNavigate } from 'react-router'
import { guardChatSwitch } from '../../../viewer/siteBoundaryEdit/chatSwitchGuard'
import { SiteBoundaryChatSwitchDialog } from '../../viewer/components/SiteBoundaryChatSwitchDialog'
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

  // specs/079 FR-029: leaving for another chat with unsaved outline changes asks first.
  const handleSelectChat = (id: string) =>
    guardChatSwitch(id, () => {
      setActiveChatId(id)
      navigate('/studio')
    })

  const handleNewChat = () =>
    guardChatSwitch(null, () => {
      setActiveChatId(null)
      navigate('/studio')
    })

  return (
    <Box sx={{ height: 480 }}>
      <ConversationList
        selectedChatId={activeChatId}
        onSelectChat={handleSelectChat}
        onNewChat={handleNewChat}
        showNewChatButton={false}
      />
      <SiteBoundaryChatSwitchDialog onReturnToEditor={() => navigate('/studio')} />
    </Box>
  )
}
