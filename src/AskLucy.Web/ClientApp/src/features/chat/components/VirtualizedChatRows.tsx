import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import PushPinIcon from '@mui/icons-material/PushPin'
import StarIcon from '@mui/icons-material/Star'
import { Box, IconButton, ListItemButton, ListItemText, Stack, TextField, Typography } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import type { ReactNode } from 'react'
import { useRef } from 'react'
import { EmptyState } from '../../../components/EmptyState'
import { radius } from '../../../theme'
import type { ConversationSummary } from '../api/chatsApi'

export interface Row {
  type: 'header' | 'item'
  header?: string
  chat?: ConversationSummary
}

interface VirtualizedChatRowsProps {
  rows: Row[]
  hasChats: boolean
  emptyIcon: ReactNode
  emptyTitle: string
  emptyDescription: string
  isFetchingNextPage: boolean
  hasNextPage: boolean | undefined
  onFetchNextPage: () => void
  selectedChatId: string | null
  editingId: string | null
  editingTitle: string
  onEditingTitleChange: (value: string) => void
  onSelectChat: (chatId: string) => void
  onStartRename: (chat: ConversationSummary) => void
  onCommitRename: (chat: ConversationSummary) => void
  onDeleteChat: (chat: ConversationSummary) => void
  onOpenMenu: (chat: ConversationSummary, event: React.MouseEvent<HTMLElement>) => void
}

/**
 * Owns `useVirtualizer()` and everything downstream of it, isolated from `ConversationList` so
 * that component keeps its React Compiler memoization. TanStack Virtual's hook returns functions
 * that cannot be memoized safely, which makes the compiler skip memoizing whichever component
 * calls it (react-hooks/incompatible-library) — confining that to this small, presentation-only
 * leaf means the much larger sidebar around it is unaffected.
 */
export function VirtualizedChatRows({
  rows,
  hasChats,
  emptyIcon,
  emptyTitle,
  emptyDescription,
  isFetchingNextPage,
  hasNextPage,
  onFetchNextPage,
  selectedChatId,
  editingId,
  editingTitle,
  onEditingTitleChange,
  onSelectChat,
  onStartRename,
  onCommitRename,
  onDeleteChat,
  onOpenMenu,
}: VirtualizedChatRowsProps) {
  const listParentRef = useRef<HTMLDivElement>(null)
  // TanStack Virtual's useVirtualizer() returns functions React Compiler cannot memoize
  // safely; isolating it to this leaf (rather than suppressing globally) is the point of
  // this extraction, so the notice here is expected and permanently accepted.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: (index) => (rows[index]?.type === 'header' ? 32 : 56),
    overscan: 10,
  })

  const handleScroll = () => {
    const el = listParentRef.current
    if (!el || isFetchingNextPage || !hasNextPage) return
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 200) {
      onFetchNextPage()
    }
  }

  return (
    <Box
      ref={listParentRef}
      onScroll={handleScroll}
      data-testid="conversation-list"
      sx={{ overflowY: 'auto', flex: 1, minHeight: 0, px: 1 }}
    >
      {!hasChats && <EmptyState icon={emptyIcon} title={emptyTitle} description={emptyDescription} />}
      <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((virtualItem) => {
          const row = rows[virtualItem.index]
          return (
            <Box
              key={virtualItem.key}
              data-index={virtualItem.index}
              ref={virtualizer.measureElement}
              sx={{
                position: 'absolute',
                top: 0,
                left: 0,
                width: '100%',
                transform: `translateY(${virtualItem.start}px)`,
              }}
            >
              {row.type === 'header' ? (
                <Typography variant="overline" color="text.secondary" sx={{ px: 1, display: 'block' }}>
                  {row.header}
                </Typography>
              ) : (
                <ConversationRow
                  chat={row.chat!}
                  selected={row.chat!.id === selectedChatId}
                  editing={editingId === row.chat!.id}
                  editingTitle={editingTitle}
                  onEditingTitleChange={onEditingTitleChange}
                  onSelect={() => onSelectChat(row.chat!.id)}
                  onStartRename={() => onStartRename(row.chat!)}
                  onCommitRename={() => onCommitRename(row.chat!)}
                  onDelete={() => onDeleteChat(row.chat!)}
                  onOpenMenu={(e) => onOpenMenu(row.chat!, e)}
                />
              )}
            </Box>
          )
        })}
      </Box>
      {isFetchingNextPage && (
        <Typography variant="caption" color="text.secondary" sx={{ px: 2, py: 1, display: 'block' }}>
          Loading more…
        </Typography>
      )}
    </Box>
  )
}

interface ConversationRowProps {
  chat: ConversationSummary
  selected: boolean
  editing: boolean
  editingTitle: string
  onEditingTitleChange: (value: string) => void
  onSelect: () => void
  onStartRename: () => void
  onCommitRename: () => void
  onDelete: () => void
  onOpenMenu: (event: React.MouseEvent<HTMLElement>) => void
}

function ConversationRow({
  chat,
  selected,
  editing,
  editingTitle,
  onEditingTitleChange,
  onSelect,
  onStartRename,
  onCommitRename,
  onDelete,
  onOpenMenu,
}: ConversationRowProps) {
  if (editing) {
    return (
      <Box sx={{ px: 1, py: 0.5 }}>
        <TextField
          size="small"
          fullWidth
          autoFocus
          value={editingTitle}
          onChange={(e) => onEditingTitleChange(e.target.value)}
          onBlur={onCommitRename}
          onKeyDown={(e) => {
            if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
          }}
        />
      </Box>
    )
  }

  const lastActivity = chat.modifiedAtUtc ?? chat.createdAtUtc

  return (
    <ListItemButton
      data-testid="conversation-item"
      selected={selected}
      onClick={onSelect}
      sx={{ borderRadius: `${radius.md}px`, mb: 0.5, '&:hover .chat-item-actions': { opacity: 1 } }}
    >
      <ListItemText
        primary={
          <Stack data-testid="conversation-title" direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
            {chat.isPinned && (
              <PushPinIcon fontSize="inherit" aria-hidden="true" sx={{ color: 'text.disabled', flexShrink: 0 }} />
            )}
            {chat.isFavorite && (
              <StarIcon fontSize="inherit" aria-hidden="true" sx={{ color: 'warning.main', flexShrink: 0 }} />
            )}
            <Typography component="span" noWrap sx={{ display: 'block' }}>
              {chat.title}
            </Typography>
          </Stack>
        }
        secondary={new Date(lastActivity).toLocaleString()}
        slotProps={{ primary: { noWrap: true }, secondary: { variant: 'caption' } }}
      />
      <Stack
        direction="row"
        className="chat-item-actions"
        sx={{ opacity: 0, transition: (t) => t.transitions.create('opacity') }}
      >
        {!chat.isDeleted && (
          <IconButton
            size="small"
            onClick={(e) => {
              e.stopPropagation()
              onStartRename()
            }}
            aria-label="Rename chat"
          >
            <EditIcon fontSize="small" />
          </IconButton>
        )}
        {!chat.isDeleted && (
          <IconButton
            size="small"
            onClick={(e) => {
              e.stopPropagation()
              onDelete()
            }}
            aria-label="Delete chat"
          >
            <DeleteIcon fontSize="small" />
          </IconButton>
        )}
        <IconButton
          size="small"
          onClick={(e) => {
            e.stopPropagation()
            onOpenMenu(e)
          }}
          aria-label="More actions"
        >
          <MoreVertIcon fontSize="small" />
        </IconButton>
      </Stack>
    </ListItemButton>
  )
}
