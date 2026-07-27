import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import {
  Box,
  IconButton,
  List,
  ListItemButton,
  ListItemText,
  Stack,
  TextField,
} from '@mui/material'
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
      }}
    >
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', p: 1 }}>
        <span>Chats</span>
        <IconButton onClick={() => createChat.mutate('New chat')} aria-label="New chat">
          <AddIcon />
        </IconButton>
      </Stack>
      <List sx={{ overflowY: 'auto', flex: 1 }}>
        {chats?.map((chat) =>
          editingId === chat.id ? (
            <Box key={chat.id} sx={{ px: 2, py: 1 }}>
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
            <ListItemButton key={chat.id}>
              <ListItemText primary={chat.title} />
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
            </ListItemButton>
          ),
        )}
      </List>
    </Box>
  )
}
