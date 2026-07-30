import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import { Box, Button, Popover } from '@mui/material'
import { useState } from 'react'
import { ConversationList } from './ChatSidebar'

interface ConversationSwitcherProps {
  selectedChatId: string | null
  onSelectChat: (id: string) => void
  onNewChat: () => void
}

/** FR-008/FR-009: replaces the permanent conversation-history sidebar with a compact
 * selector at the top of the assistant panel. The Popover gets a fixed height (not just
 * a max-height) so `ConversationList`'s internal `height: '100%'` — and therefore its
 * `useVirtualizer` scroll container — has a definite size to resolve against, rather
 * than the full-height column it originally assumed (research.md §7). */
export function ConversationSwitcher({
  selectedChatId,
  onSelectChat,
  onNewChat,
}: ConversationSwitcherProps) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const open = Boolean(anchorEl)

  return (
    <Box sx={{ px: 1, pt: 1 }}>
      <Button
        fullWidth
        variant="text"
        endIcon={<ExpandMoreIcon />}
        onClick={(e) => setAnchorEl(e.currentTarget)}
        aria-haspopup="true"
        aria-expanded={open}
        sx={{ justifyContent: 'space-between', textTransform: 'none' }}
      >
        Conversations
      </Button>
      <Popover
        open={open}
        anchorEl={anchorEl}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{
          paper: {
            sx: {
              width: 360,
              maxWidth: '90vw',
              height: 420,
              display: 'flex',
              flexDirection: 'column',
            },
          },
        }}
      >
        <ConversationList
          selectedChatId={selectedChatId}
          onSelectChat={(id) => {
            onSelectChat(id)
            setAnchorEl(null)
          }}
          onNewChat={() => {
            onNewChat()
            setAnchorEl(null)
          }}
        />
      </Popover>
    </Box>
  )
}
