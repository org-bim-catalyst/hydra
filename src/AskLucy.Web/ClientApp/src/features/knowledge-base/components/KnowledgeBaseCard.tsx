import ArchiveIcon from '@mui/icons-material/Archive'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import DeleteForeverIcon from '@mui/icons-material/DeleteForever'
import DeleteIcon from '@mui/icons-material/Delete'
import DescriptionIcon from '@mui/icons-material/Description'
import DownloadIcon from '@mui/icons-material/Download'
import EditIcon from '@mui/icons-material/Edit'
import FolderIcon from '@mui/icons-material/Folder'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import PublishedWithChangesIcon from '@mui/icons-material/PublishedWithChanges'
import PushPinIcon from '@mui/icons-material/PushPin'
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined'
import RestoreIcon from '@mui/icons-material/Restore'
import StarIcon from '@mui/icons-material/Star'
import StarOutlineIcon from '@mui/icons-material/StarBorder'
import {
  Card,
  CardContent,
  Chip,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Stack,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { KnowledgeBaseSummary } from '../api/knowledgeBasesApi'

function formatStorageSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

const STATUS_COLOR: Record<KnowledgeBaseSummary['status'], 'default' | 'success' | 'warning'> = {
  Draft: 'default',
  Active: 'success',
  Archived: 'warning',
}

interface KnowledgeBaseCardProps {
  knowledgeBase: KnowledgeBaseSummary
  /** Resolved client-side from the categories list (FR-017/FR-018) — `KnowledgeBaseSummary` only carries `categoryId`, not a name. Undefined/no match renders as "Uncategorized". */
  categoryName?: string
  /** Opens the knowledge base's folder/document detail view (US2) — omitted for the Deleted view, where a card isn't openable. */
  onOpen?: () => void
  onEdit: () => void
  onActivate: () => void
  onArchive: () => void
  onDelete: () => void
  onRestore: () => void
  onPurge: () => void
  /** Quick favorite/pin toggles (FR-027/FR-028) — omitted for the Deleted view, where neither action applies. */
  onToggleFavorite?: () => void
  onTogglePin?: () => void
  /** FR-032/FR-033 (US6) — omitted for the Deleted view alongside the other non-destructive actions. */
  onDuplicate?: () => void
  onExport?: () => void
}

/**
 * A single knowledge base's dashboard tile (FR-026). Status is shown as a text `Chip`, not a
 * bare color swatch — color alone must never be the only signal (FR-041).
 */
export function KnowledgeBaseCard({
  knowledgeBase,
  categoryName,
  onOpen,
  onEdit,
  onActivate,
  onArchive,
  onDelete,
  onRestore,
  onPurge,
  onToggleFavorite,
  onTogglePin,
  onDuplicate,
  onExport,
}: KnowledgeBaseCardProps) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  const closeMenu = () => setAnchorEl(null)

  return (
    <Card
      data-testid="knowledge-base-card"
      variant="outlined"
      sx={{
        borderLeft: 4,
        borderLeftColor: knowledgeBase.color || 'divider',
        cursor: onOpen ? 'pointer' : undefined,
        transition: (theme) => theme.transitions.create(['box-shadow', 'border-color']),
        ...(onOpen && {
          '&:hover': { boxShadow: (theme) => theme.shadows[2], borderColor: 'text.disabled' },
        }),
      }}
      onClick={onOpen}
    >
      <CardContent>
        <Stack direction="row" sx={{ alignItems: 'flex-start', justifyContent: 'space-between' }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
            <FolderIcon fontSize="small" color="action" aria-hidden />
            <Typography variant="subtitle1" data-testid="knowledge-base-name" noWrap>
              {knowledgeBase.name}
            </Typography>
          </Stack>
          <Stack direction="row" sx={{ alignItems: 'center' }}>
            {onTogglePin && (
              <IconButton
                size="small"
                aria-label="Pin"
                aria-pressed={knowledgeBase.isPinned}
                onClick={(e) => {
                  e.stopPropagation()
                  onTogglePin()
                }}
              >
                {knowledgeBase.isPinned ? (
                  <PushPinIcon fontSize="small" color="primary" titleAccess="Pinned" />
                ) : (
                  <PushPinOutlinedIcon fontSize="small" />
                )}
              </IconButton>
            )}
            {onToggleFavorite && (
              <IconButton
                size="small"
                aria-label="Favorite"
                aria-pressed={knowledgeBase.isFavorite}
                onClick={(e) => {
                  e.stopPropagation()
                  onToggleFavorite()
                }}
              >
                {knowledgeBase.isFavorite ? (
                  <StarIcon fontSize="small" color="warning" titleAccess="Favorite" />
                ) : (
                  <StarOutlineIcon fontSize="small" />
                )}
              </IconButton>
            )}
            <IconButton
              size="small"
              aria-label="More actions"
              onClick={(e) => {
                e.stopPropagation()
                setAnchorEl(e.currentTarget)
              }}
            >
              <MoreVertIcon fontSize="small" />
            </IconButton>
          </Stack>
        </Stack>

        {knowledgeBase.description && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 1, mb: 1.5 }}>
            {knowledgeBase.description}
          </Typography>
        )}

        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
          <Chip
            size="small"
            label={knowledgeBase.status}
            color={STATUS_COLOR[knowledgeBase.status]}
            data-testid="knowledge-base-status"
          />
          {/* Null categoryId is always "Uncategorized"; a set categoryId with no resolved name yet (categories still loading) renders nothing rather than a wrong flash of "Uncategorized" (FR-021). */}
          {!knowledgeBase.categoryId && <Chip size="small" variant="outlined" label="Uncategorized" />}
          {knowledgeBase.categoryId && categoryName && <Chip size="small" variant="outlined" label={categoryName} />}
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
            <DescriptionIcon fontSize="inherit" color="action" aria-hidden />
            <Typography variant="caption" color="text.secondary">
              {knowledgeBase.documentCount} document{knowledgeBase.documentCount === 1 ? '' : 's'}
            </Typography>
          </Stack>
          <Typography variant="caption" color="text.secondary">
            {formatStorageSize(knowledgeBase.storageSizeBytes)}
          </Typography>
        </Stack>

        {knowledgeBase.tags.length > 0 && (
          <Stack direction="row" spacing={0.5} sx={{ mt: 1.5, flexWrap: 'wrap', gap: 0.5 }}>
            {knowledgeBase.tags.map((tag) => (
              <Chip key={tag} size="small" variant="outlined" label={tag} />
            ))}
          </Stack>
        )}
      </CardContent>

      {/* MUI's Menu renders via a Portal, but React's synthetic events bubble the component
          tree, not the DOM tree — without this, clicking a menu item would also fire the
          Card's onClick and navigate away right as an action (e.g. Edit) opens. */}
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={closeMenu} onClick={(e) => e.stopPropagation()}>
        {!knowledgeBase.isDeleted && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onEdit()
            }}
          >
            <ListItemIcon>
              <EditIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Edit</ListItemText>
          </MenuItem>
        )}
        {!knowledgeBase.isDeleted && knowledgeBase.status === 'Draft' && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onActivate()
            }}
          >
            <ListItemIcon>
              <PublishedWithChangesIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Activate</ListItemText>
          </MenuItem>
        )}
        {!knowledgeBase.isDeleted && knowledgeBase.status === 'Active' && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onArchive()
            }}
          >
            <ListItemIcon>
              <ArchiveIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Archive</ListItemText>
          </MenuItem>
        )}
        {onDuplicate && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onDuplicate()
            }}
          >
            <ListItemIcon>
              <ContentCopyIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Duplicate</ListItemText>
          </MenuItem>
        )}
        {onExport && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onExport()
            }}
          >
            <ListItemIcon>
              <DownloadIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Export</ListItemText>
          </MenuItem>
        )}
        {!knowledgeBase.isDeleted && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onDelete()
            }}
          >
            <ListItemIcon>
              <DeleteIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Delete</ListItemText>
          </MenuItem>
        )}
        {(knowledgeBase.isDeleted || knowledgeBase.status === 'Archived') && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onRestore()
            }}
          >
            <ListItemIcon>
              <RestoreIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Restore</ListItemText>
          </MenuItem>
        )}
        {knowledgeBase.isDeleted && (
          <MenuItem
            onClick={() => {
              closeMenu()
              onPurge()
            }}
          >
            <ListItemIcon>
              <DeleteForeverIcon fontSize="small" color="error" />
            </ListItemIcon>
            <ListItemText sx={{ color: 'error.main' }}>Delete permanently</ListItemText>
          </MenuItem>
        )}
      </Menu>
    </Card>
  )
}
