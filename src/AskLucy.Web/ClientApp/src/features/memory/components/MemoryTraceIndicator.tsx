import { useState, type MouseEvent } from 'react'
import PsychologyAltOutlinedIcon from '@mui/icons-material/PsychologyAltOutlined'
import { Box, Chip, CircularProgress, List, ListItem, ListItemText, Popover, Typography } from '@mui/material'
import { useMemoryReferences } from '../hooks/useMemoryReferences'

/**
 * spec.md FR-014, tasks.md T044 — the "why does Lucy know this" trace. Only mounted by
 * `MessageBubble` when `message.memoryOutcome === 'Found'` (so its `useMemoryReferences` query
 * never runs for the vast majority of messages that never used memory), and only fetches once
 * the user actually opens it.
 */
export function MemoryTraceIndicator({ chatId, messageId }: { chatId: string; messageId: string }) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const open = Boolean(anchorEl)
  const { data: references, isLoading } = useMemoryReferences(chatId, messageId, open)

  const handleOpen = (event: MouseEvent<HTMLElement>) => setAnchorEl(event.currentTarget)
  const handleClose = () => setAnchorEl(null)

  return (
    <>
      <Chip
        size="small"
        variant="outlined"
        icon={<PsychologyAltOutlinedIcon />}
        label="Lucy remembered this"
        onClick={handleOpen}
        clickable
        sx={{ mt: 1 }}
      />
      <Popover
        open={open}
        anchorEl={anchorEl}
        onClose={handleClose}
        anchorOrigin={{ vertical: 'top', horizontal: 'left' }}
        transformOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      >
        <Box sx={{ p: 2, maxWidth: 360 }}>
          <Typography variant="subtitle2" gutterBottom>
            What Lucy remembered for this response
          </Typography>
          {isLoading && <CircularProgress size={20} />}
          {!isLoading && (references?.length ?? 0) === 0 && (
            <Typography variant="body2" color="text.secondary">
              Nothing to show.
            </Typography>
          )}
          {!isLoading && references && references.length > 0 && (
            <List dense disablePadding>
              {references.map((reference) => (
                <ListItem key={reference.memoryId} disableGutters>
                  <ListItemText primary={reference.content} />
                </ListItem>
              ))}
            </List>
          )}
        </Box>
      </Popover>
    </>
  )
}
