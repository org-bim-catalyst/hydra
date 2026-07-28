import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import { Box, Button, IconButton, List, ListItemButton, ListItemText, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useChats, useCreateChat, useDeleteChat, useRenameChat } from '../hooks/useChats'

/** FR-008/FR-033: create, rename, and delete saved chats. */
export function ChatSidebar() {
  const { data: chats } = useChats()
  const createChat = useCreateChat()
  const renameChat = useRenameChat()
  const deleteChat = useDeleteChat()
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingTitle, setEditingTitle] = useState('')

  return (
    <Box
      sx={{
        width: 280,
        borderRight: 1,
        borderColor: 'divider',
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        bgcolor: 'background.default',
      }}
    >
      <Box sx={{ p: 1.5 }}>
        <Button
          fullWidth
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={() => createChat.mutate('New chat')}
          sx={{ justifyContent: 'flex-start', bgcolor: 'background.paper' }}
        >
          New chat
        </Button>
      </Box>
      <List sx={{ overflowY: 'auto', flex: 1, px: 1 }}>
        {chats?.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ px: 2, py: 1 }}>
            No conversations yet.
          </Typography>
        )}
        {chats?.map((chat) =>
          editingId === chat.id ? (
            <Box key={chat.id} sx={{ px: 1, py: 0.5 }}>
              <TextField
                size="small"
                fullWidth
                autoFocus
                value={editingTitle}
                onChange={(e) => setEditingTitle(e.target.value)}
                onBlur={() => {
                  if (editingTitle.trim()) renameChat.mutate({ id: chat.id, title: editingTitle.trim() })
                  setEditingId(null)
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
                }}
              />
            </Box>
          ) : (
            <ListItemButton
              key={chat.id}
              sx={{ borderRadius: 2, mb: 0.5, '&:hover .chat-item-actions': { opacity: 1 } }}
            >
              <ListItemText primary={chat.title} slotProps={{ primary: { noWrap: true } }} />
              <Stack direction="row" className="chat-item-actions" sx={{ opacity: 0, transition: 'opacity 150ms' }}>
                <IconButton
                  size="small"
                  onClick={(e) => {
                    e.stopPropagation()
                    setEditingId(chat.id)
                    setEditingTitle(chat.title)
                  }}
                  aria-label="Rename chat"
                >
                  <EditIcon fontSize="small" />
                </IconButton>
                <IconButton
                  size="small"
                  onClick={(e) => {
                    e.stopPropagation()
                    deleteChat.mutate(chat.id)
                  }}
                  aria-label="Delete chat"
                >
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </Stack>
            </ListItemButton>
          ),
        )}
      </List>
    </Box>
  )
}
