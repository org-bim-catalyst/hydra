import AddIcon from '@mui/icons-material/Add'
import ArchiveIcon from '@mui/icons-material/Archive'
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutlineOutlined'
import DeleteForeverIcon from '@mui/icons-material/DeleteForever'
import DeleteIcon from '@mui/icons-material/Delete'
import DownloadIcon from '@mui/icons-material/Download'
import FileCopyIcon from '@mui/icons-material/FileCopy'
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
  InputAdornment,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Snackbar,
  Stack,
  TextField,
} from '@mui/material'
import { useMemo, useState } from 'react'
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
import { VirtualizedChatRows, type Row } from './VirtualizedChatRows'

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
 * container, not just a full-height column. This component itself makes no assumption
 * about its container's width/height beyond filling it (`height: '100%'`); the caller
 * supplies both the size and any chrome around it — specs/025-chat-configuration-settings
 * relocated it from an in-workspace `ConversationSwitcher` Popover (now deleted) into the
 * standalone Chat History Settings tab (`ChatHistoryTab.tsx`).
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

      <VirtualizedChatRows
        rows={rows}
        hasChats={chats.length > 0}
        emptyIcon={<ChatBubbleOutlineIcon fontSize="inherit" />}
        emptyTitle={searchInput.trim() || filter !== 'all' ? 'No matching conversations' : 'No conversations yet'}
        emptyDescription={
          searchInput.trim() || filter !== 'all'
            ? 'Try a different search term or filter.'
            : 'Start a new chat to begin.'
        }
        isFetchingNextPage={isFetchingNextPage}
        hasNextPage={hasNextPage}
        onFetchNextPage={() => void fetchNextPage()}
        selectedChatId={selectedChatId}
        editingId={editingId}
        editingTitle={editingTitle}
        onEditingTitleChange={setEditingTitle}
        onSelectChat={onSelectChat}
        onStartRename={(chat) => {
          setEditingId(chat.id)
          setEditingTitle(chat.title)
        }}
        onCommitRename={(chat) => {
          if (editingTitle.trim()) renameChat.mutate({ id: chat.id, title: editingTitle.trim() })
          setEditingId(null)
        }}
        onDeleteChat={(chat) => {
          deleteChat.mutate(chat.id)
          if (chat.id === selectedChatId) onNewChat()
        }}
        onOpenMenu={(chat, e) => {
          setMenuAnchor(e.currentTarget)
          setMenuChat(chat)
        }}
      />

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

/** The original fixed 300px permanent column shell around `ConversationList`. No longer used
 * anywhere in the app (superseded first by the in-workspace `ConversationSwitcher` popover,
 * then by the standalone Chat History Settings tab — specs/025-chat-configuration-settings),
 * kept as the standalone, directly-testable entry point it always was. */
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

