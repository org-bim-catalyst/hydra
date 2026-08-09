import AddIcon from '@mui/icons-material/Add'
import ArchiveIcon from '@mui/icons-material/Archive'
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutlineOutlined'
import DeleteForeverIcon from '@mui/icons-material/DeleteForever'
import DeleteIcon from '@mui/icons-material/Delete'
import DownloadIcon from '@mui/icons-material/Download'
import EditIcon from '@mui/icons-material/Edit'
import FileCopyIcon from '@mui/icons-material/FileCopy'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import RestoreIcon from '@mui/icons-material/Restore'
import SearchIcon from '@mui/icons-material/Search'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import PushPinIcon from '@mui/icons-material/PushPin'
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined'
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  InputAdornment,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Snackbar,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useMemo, useRef, useState } from 'react'
import type { ConversationSort, ConversationSummary, ConversationView } from '../api/chatsApi'
import * as chatsApi from '../api/chatsApi'
import { useDeleteChat, useRenameChat, useSearchChats } from '../hooks/useChats'
import {
  useArchiveChat,
  useClearChatMessages,
  useDuplicateChat,
  useFavoriteChat,
  usePinChat,
  usePurgeChat,
  useRestoreChat,
  useUnfavoriteChat,
  useUnpinChat,
} from '../hooks/useConversationActions'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { EmptyState } from '../../../components/EmptyState'
import { radius } from '../../../theme'

interface ConversationListProps {
  selectedChatId: string | null
  onSelectChat: (id: string) => void
  onNewChat: () => void
}

type FilterChip = 'all' | 'favorite' | 'archived' | 'pinned' | 'deleted'

const FILTERS: { value: FilterChip; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'favorite', label: 'Favorites' },
  { value: 'pinned', label: 'Pinned' },
  { value: 'archived', label: 'Archived' },
  { value: 'deleted', label: 'Recently Deleted' },
]

const SORTS: { value: ConversationSort; label: string }[] = [
  { value: 'Newest', label: 'Newest' },
  { value: 'Oldest', label: 'Oldest' },
  { value: 'RecentlyUpdated', label: 'Recently updated' },
  { value: 'Alphabetical', label: 'Alphabetical' },
]

function filterToParams(filter: FilterChip): {
  view: ConversationView
  pinned?: boolean
  favorite?: boolean
} {
  switch (filter) {
    case 'favorite':
      return { view: 'Active', favorite: true }
    case 'pinned':
      return { view: 'Active', pinned: true }
    case 'archived':
      return { view: 'Archived' }
    case 'deleted':
      return { view: 'Deleted' }
    default:
      return { view: 'Active' }
  }
}

/** FR-012/FR-023: relative recency heading a conversation's last-updated timestamp falls into. */
function dateGroupFor(dateIso: string | null, createdIso: string): string {
  const date = new Date(dateIso ?? createdIso)
  const now = new Date()
  const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  const daysAgo = Math.floor(
    (startOfToday.getTime() -
      new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()) /
      86_400_000,
  )

  if (daysAgo <= 0) return 'Today'
  if (daysAgo === 1) return 'Yesterday'
  if (daysAgo <= 7) return 'Previous 7 Days'
  if (daysAgo <= 30) return 'Previous 30 Days'
  return 'Older'
}

interface Row {
  type: 'header' | 'item'
  header?: string
  chat?: ConversationSummary
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

/**
 * FR-008/FR-019–FR-024: create (deferred to the first message sent, see useChatStream's
 * ensureChatId), search, filter, sort, virtualized infinite-scroll list, date grouping,
 * rename, delete, and select a saved chat to load its history. The per-item "more actions"
 * menu adds pin/favorite/archive/restore/duplicate/clear/export/permanent-delete (User
 * Stories 3–5), each using optimistic mutations (useConversationActions.ts) with a Snackbar
 * surfacing any failure (constitution §2.VIII No Silent Failures, SC-005).
 *
 * Extracted from the old fixed-width `ChatSidebar` shell (research.md §7, spec.md FR-008)
 * so the same list/search/filter/sort/action logic can be reused inside a bounded-height
 * container — a `ConversationSwitcher` Popover — not just a full-height column. This
 * component itself makes no assumption about its container's width/height beyond filling
 * it (`height: '100%'`); the caller supplies both the size and any chrome around it.
 */
export function ConversationList({
  selectedChatId,
  onSelectChat,
  onNewChat,
}: ConversationListProps) {
  const [filter, setFilter] = useState<FilterChip>('all')
  const [sort, setSort] = useState<ConversationSort>('Newest')
  const [searchInput, setSearchInput] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingTitle, setEditingTitle] = useState('')
  const [menuChat, setMenuChat] = useState<ConversationSummary | null>(null)
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null)
  const [confirmAction, setConfirmAction] = useState<{
    chatId: string
    kind: 'clear' | 'purge'
  } | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  const { view, pinned, favorite } = filterToParams(filter)
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useSearchChats({
    view,
    pinned,
    favorite,
    sort,
    q: searchInput.trim() || undefined,
    pageSize: 30,
  })
  const renameChat = useRenameChat()
  const deleteChat = useDeleteChat()
  const archiveChat = useArchiveChat()
  const restoreChat = useRestoreChat()
  const pinChat = usePinChat()
  const unpinChat = useUnpinChat()
  const favoriteChat = useFavoriteChat()
  const unfavoriteChat = useUnfavoriteChat()
  const duplicateChat = useDuplicateChat()
  const clearChatMessages = useClearChatMessages()
  const purgeChat = usePurgeChat()

  const chats = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])

  const rows = useMemo<Row[]>(() => {
    if (sort !== 'Newest' && sort !== 'RecentlyUpdated') {
      return chats.map((chat) => ({ type: 'item', chat }))
    }

    const result: Row[] = []
    let lastGroup: string | null = null
    for (const chat of chats) {
      const group = dateGroupFor(chat.modifiedAtUtc, chat.createdAtUtc)
      if (group !== lastGroup) {
        result.push({ type: 'header', header: group })
        lastGroup = group
      }
      result.push({ type: 'item', chat })
    }
    return result
  }, [chats, sort])

  const listParentRef = useRef<HTMLDivElement>(null)
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
      void fetchNextPage()
    }
  }

  const closeMenu = () => {
    setMenuAnchor(null)
    setMenuChat(null)
  }

  const runAction = (action: () => Promise<unknown>) => {
    closeMenu()
    action().catch((err) =>
      setActionError(err instanceof Error ? err.message : 'Action failed. Please try again.'),
    )
  }

  const handleExport = async (chatId: string, title: string) => {
    closeMenu()
    try {
      const blob = await chatsApi.exportChat(chatId)
      downloadBlob(blob, `${title || 'conversation'}.json`)
    } catch (err) {
      setActionError(err instanceof Error ? err.message : 'Export failed. Please try again.')
    }
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
      <Box sx={{ p: 1.5 }}>
        <Button
          fullWidth
          variant="outlined"
          startIcon={<AddIcon />}
          onClick={onNewChat}
          sx={{ justifyContent: 'flex-start', bgcolor: 'background.paper' }}
        >
          New chat
        </Button>
      </Box>

      <Box sx={{ px: 1.5, pb: 1 }}>
        <TextField
          fullWidth
          size="small"
          placeholder="Search conversations"
          aria-label="Search conversations"
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
        />
      </Box>

      <Stack direction="row" spacing={0.5} sx={{ px: 1.5, pb: 1, flexWrap: 'wrap', rowGap: 0.5 }}>
        {FILTERS.map((f) => (
          <Chip
            key={f.value}
            label={f.label}
            size="small"
            color={filter === f.value ? 'primary' : 'default'}
            onClick={() => setFilter(f.value)}
          />
        ))}
      </Stack>

      <Box sx={{ px: 1.5, pb: 1 }}>
        <TextField
          select
          fullWidth
          size="small"
          label="Sort"
          aria-label="Sort conversations"
          value={sort}
          onChange={(e) => setSort(e.target.value as ConversationSort)}
        >
          {SORTS.map((s) => (
            <MenuItem key={s.value} value={s.value}>
              {s.label}
            </MenuItem>
          ))}
        </TextField>
      </Box>

      <Box
        ref={listParentRef}
        onScroll={handleScroll}
        data-testid="conversation-list"
        sx={{ overflowY: 'auto', flex: 1, minHeight: 0, px: 1 }}
      >
        {chats.length === 0 && (
          <EmptyState
            icon={<ChatBubbleOutlineIcon fontSize="inherit" />}
            title={searchInput.trim() || filter !== 'all' ? 'No matching conversations' : 'No conversations yet'}
            description={
              searchInput.trim() || filter !== 'all'
                ? 'Try a different search term or filter.'
                : 'Start a new chat to begin.'
            }
          />
        )}
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
                  <Typography
                    variant="overline"
                    color="text.secondary"
                    sx={{ px: 1, display: 'block' }}
                  >
                    {row.header}
                  </Typography>
                ) : (
                  <ConversationRow
                    chat={row.chat!}
                    selected={row.chat!.id === selectedChatId}
                    editing={editingId === row.chat!.id}
                    editingTitle={editingTitle}
                    onEditingTitleChange={setEditingTitle}
                    onSelect={() => onSelectChat(row.chat!.id)}
                    onStartRename={() => {
                      setEditingId(row.chat!.id)
                      setEditingTitle(row.chat!.title)
                    }}
                    onCommitRename={() => {
                      if (editingTitle.trim())
                        renameChat.mutate({ id: row.chat!.id, title: editingTitle.trim() })
                      setEditingId(null)
                    }}
                    onDelete={() => {
                      deleteChat.mutate(row.chat!.id)
                      if (row.chat!.id === selectedChatId) onNewChat()
                    }}
                    onOpenMenu={(e) => {
                      setMenuAnchor(e.currentTarget)
                      setMenuChat(row.chat!)
                    }}
                  />
                )}
              </Box>
            )
          })}
        </Box>
        {isFetchingNextPage && (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{ px: 2, py: 1, display: 'block' }}
          >
            Loading more…
          </Typography>
        )}
      </Box>

      <Menu anchorEl={menuAnchor} open={Boolean(menuAnchor)} onClose={closeMenu}>
        {menuChat?.isDeleted
          ? [
              <MenuItem
                key="restore"
                onClick={() => runAction(() => restoreChat.mutateAsync(menuChat.id))}
              >
                <ListItemIcon>
                  <RestoreIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Restore</ListItemText>
              </MenuItem>,
              <MenuItem
                key="purge"
                onClick={() => {
                  setConfirmAction({ chatId: menuChat.id, kind: 'purge' })
                  closeMenu()
                }}
              >
                <ListItemIcon>
                  <DeleteForeverIcon fontSize="small" color="error" />
                </ListItemIcon>
                <ListItemText>Delete permanently</ListItemText>
              </MenuItem>,
            ]
          : [
              <MenuItem
                key="pin"
                onClick={() =>
                  runAction(() =>
                    menuChat!.isPinned
                      ? unpinChat.mutateAsync(menuChat!.id)
                      : pinChat.mutateAsync(menuChat!.id),
                  )
                }
              >
                <ListItemIcon>
                  {menuChat?.isPinned ? (
                    <PushPinIcon fontSize="small" />
                  ) : (
                    <PushPinOutlinedIcon fontSize="small" />
                  )}
                </ListItemIcon>
                <ListItemText>{menuChat?.isPinned ? 'Unpin' : 'Pin'}</ListItemText>
              </MenuItem>,
              <MenuItem
                key="favorite"
                onClick={() =>
                  runAction(() =>
                    menuChat!.isFavorite
                      ? unfavoriteChat.mutateAsync(menuChat!.id)
                      : favoriteChat.mutateAsync(menuChat!.id),
                  )
                }
              >
                <ListItemIcon>
                  {menuChat?.isFavorite ? (
                    <StarIcon fontSize="small" />
                  ) : (
                    <StarBorderIcon fontSize="small" />
                  )}
                </ListItemIcon>
                <ListItemText>{menuChat?.isFavorite ? 'Unfavorite' : 'Favorite'}</ListItemText>
              </MenuItem>,
              menuChat?.isArchived ? (
                <MenuItem
                  key="restore"
                  onClick={() => runAction(() => restoreChat.mutateAsync(menuChat.id))}
                >
                  <ListItemIcon>
                    <RestoreIcon fontSize="small" />
                  </ListItemIcon>
                  <ListItemText>Restore from Archive</ListItemText>
                </MenuItem>
              ) : (
                <MenuItem
                  key="archive"
                  onClick={() => runAction(() => archiveChat.mutateAsync(menuChat!.id))}
                >
                  <ListItemIcon>
                    <ArchiveIcon fontSize="small" />
                  </ListItemIcon>
                  <ListItemText>Archive</ListItemText>
                </MenuItem>
              ),
              <MenuItem
                key="duplicate"
                onClick={() => runAction(() => duplicateChat.mutateAsync(menuChat!.id))}
              >
                <ListItemIcon>
                  <FileCopyIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Duplicate</ListItemText>
              </MenuItem>,
              <MenuItem
                key="export"
                onClick={() => void handleExport(menuChat!.id, menuChat!.title)}
              >
                <ListItemIcon>
                  <DownloadIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Export</ListItemText>
              </MenuItem>,
              <MenuItem
                key="clear"
                onClick={() => {
                  setConfirmAction({ chatId: menuChat!.id, kind: 'clear' })
                  closeMenu()
                }}
              >
                <ListItemIcon>
                  <DeleteIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText>Clear messages</ListItemText>
              </MenuItem>,
            ]}
      </Menu>

      <ConfirmDialog
        open={confirmAction !== null}
        title={
          confirmAction?.kind === 'purge'
            ? 'Permanently delete this conversation?'
            : 'Clear all messages?'
        }
        description={
          confirmAction?.kind === 'purge'
            ? 'This cannot be undone. The conversation and all its messages will be permanently removed.'
            : 'This will remove all messages from this conversation. The conversation itself will remain, with its title.'
        }
        confirmLabel={confirmAction?.kind === 'purge' ? 'Delete permanently' : 'Clear messages'}
        onCancel={() => setConfirmAction(null)}
        onConfirm={() => {
          if (!confirmAction) return
          const { chatId, kind } = confirmAction
          setConfirmAction(null)
          const action =
            kind === 'purge' ? purgeChat.mutateAsync(chatId) : clearChatMessages.mutateAsync(chatId)
          action
            .then(() => {
              if (kind === 'purge' && chatId === selectedChatId) onNewChat()
            })
            .catch((err) =>
              setActionError(
                err instanceof Error ? err.message : 'Action failed. Please try again.',
              ),
            )
        }}
      />

      <Snackbar
        open={Boolean(actionError)}
        autoHideDuration={5000}
        onClose={() => setActionError(null)}
      >
        <Alert severity="error" variant="filled" onClose={() => setActionError(null)}>
          {actionError}
        </Alert>
      </Snackbar>
    </Box>
  )
}

interface ChatSidebarProps {
  selectedChatId: string | null
  onSelectChat: (id: string) => void
  onNewChat: () => void
}

/** The original fixed 300px permanent column shell around `ConversationList`. No longer
 * used by ChatPage (FR-008 replaced it with `ConversationSwitcher`'s popover), kept as
 * the standalone, directly-testable entry point it always was. */
export function ChatSidebar({ selectedChatId, onSelectChat, onNewChat }: ChatSidebarProps) {
  return (
    <Box
      sx={{
        width: 300,
        borderRight: 1,
        borderColor: 'divider',
        height: '100%',
        bgcolor: 'background.default',
      }}
    >
      <ConversationList
        selectedChatId={selectedChatId}
        onSelectChat={onSelectChat}
        onNewChat={onNewChat}
      />
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
          <Stack
            data-testid="conversation-title"
            direction="row"
            spacing={0.5}
            sx={{ alignItems: 'center' }}
          >
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
