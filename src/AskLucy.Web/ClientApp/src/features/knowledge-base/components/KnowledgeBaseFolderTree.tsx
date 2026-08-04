import { DndContext, useDraggable, useDroppable, type DragEndEvent } from '@dnd-kit/core'
import AddIcon from '@mui/icons-material/Add'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import DeleteIcon from '@mui/icons-material/Delete'
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove'
import EditIcon from '@mui/icons-material/Edit'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import FolderIcon from '@mui/icons-material/Folder'
import InsertDriveFileIcon from '@mui/icons-material/InsertDriveFile'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Snackbar,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { KnowledgeBaseDocument, KnowledgeBaseFolder } from '../api/knowledgeBaseFoldersApi'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import {
  buildFolderTree,
  useKnowledgeBaseDndSensors,
  type FolderTreeNode,
} from '../hooks/useKnowledgeBaseDragAndDrop'
import {
  useCreateFolder,
  useDeleteDocument,
  useDeleteFolder,
  useDocuments,
  useFolderTree,
  useMoveDocument,
  useMoveFolder,
  useRenameFolder,
} from '../hooks/useKnowledgeBaseFolders'

interface KnowledgeBaseFolderTreeProps {
  knowledgeBaseId: string
}

type DragItem = { type: 'folder' | 'document'; id: string }

/**
 * The folder tree + document organization view (FR-012–FR-016). Mouse drag-and-drop via
 * `@dnd-kit`; the keyboard-accessible equivalent (FR-040) is each item's "Move to…" menu
 * action (see `useKnowledgeBaseDragAndDrop.ts`'s doc comment for why, not a custom keyboard-
 * drag gesture). `role="tree"`/`role="treeitem"`/`aria-expanded` throughout for correct
 * assistive-technology semantics (FR-039).
 */
export function KnowledgeBaseFolderTree({ knowledgeBaseId }: KnowledgeBaseFolderTreeProps) {
  const { data } = useFolderTree(knowledgeBaseId)
  const createFolder = useCreateFolder(knowledgeBaseId)
  const moveFolder = useMoveFolder(knowledgeBaseId)
  const moveDocument = useMoveDocument(knowledgeBaseId)

  const [newFolderParentId, setNewFolderParentId] = useState<string | null | undefined>(undefined)
  const [movePickerItem, setMovePickerItem] = useState<DragItem | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const reportError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  const sensors = useKnowledgeBaseDndSensors()
  const folders = data?.folders ?? []
  const tree = buildFolderTree(folders);

  const handleDragEnd = (event: DragEndEvent) => {
    const dragged = event.active.data.current as DragItem | undefined
    const targetFolderId = (event.over?.data.current as { folderId: string | null } | undefined)?.folderId
    if (!dragged || targetFolderId === undefined) return

    if (dragged.type === 'folder') {
      if (dragged.id === targetFolderId) return
      moveFolder.mutate({ folderId: dragged.id, newParentFolderId: targetFolderId }, { onError: reportError })
    } else {
      moveDocument.mutate({ documentId: dragged.id, newFolderId: targetFolderId }, { onError: reportError })
    }
  }

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
        <Button size="small" startIcon={<AddIcon />} onClick={() => setNewFolderParentId(null)}>
          New Folder
        </Button>
      </Box>

      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        <RootDropZone folderId={null}>
          <List role="tree" aria-label="Knowledge base folders and documents" dense>
            {tree.map((node) => (
              <FolderTreeItem
                key={node.folder.id}
                node={node}
                knowledgeBaseId={knowledgeBaseId}
                depth={0}
                allFolders={folders}
                onNewSubfolder={(parentId) => setNewFolderParentId(parentId)}
                onMoveItem={(item) => setMovePickerItem(item)}
                onError={reportError}
              />
            ))}
            {(data?.rootDocuments ?? []).map((doc) => (
              <DocumentRow
                key={doc.id}
                document={doc}
                knowledgeBaseId={knowledgeBaseId}
                onMoveItem={(item) => setMovePickerItem(item)}
                onError={reportError}
              />
            ))}
          </List>
        </RootDropZone>
      </DndContext>

      {(data?.folders.length ?? 0) === 0 && (data?.rootDocuments.length ?? 0) === 0 && (
        <Typography color="text.secondary" sx={{ mt: 2 }}>
          No folders or documents yet.
        </Typography>
      )}

      <Dialog open={newFolderParentId !== undefined} onClose={() => setNewFolderParentId(undefined)}>
        <NewFolderForm
          submitting={createFolder.isPending}
          onCancel={() => setNewFolderParentId(undefined)}
          onSubmit={(name) => {
            createFolder.mutate(
              { name, parentFolderId: newFolderParentId ?? null },
              { onSuccess: () => setNewFolderParentId(undefined), onError: reportError },
            )
          }}
        />
      </Dialog>

      <MoveToPicker
        open={movePickerItem !== null}
        folders={folders}
        onCancel={() => setMovePickerItem(null)}
        onPick={(targetFolderId) => {
          if (!movePickerItem) return
          const item = movePickerItem
          setMovePickerItem(null)
          if (item.type === 'folder') {
            moveFolder.mutate({ folderId: item.id, newParentFolderId: targetFolderId }, { onError: reportError })
          } else {
            moveDocument.mutate({ documentId: item.id, newFolderId: targetFolderId }, { onError: reportError })
          }
        }}
      />

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </Box>
  )
}

function RootDropZone({ folderId, children }: { folderId: string | null; children: React.ReactNode }) {
  const { setNodeRef } = useDroppable({ id: 'root-drop-zone', data: { folderId } })
  return <Box ref={setNodeRef}>{children}</Box>
}

interface FolderTreeItemProps {
  node: FolderTreeNode
  knowledgeBaseId: string
  depth: number
  allFolders: KnowledgeBaseFolder[]
  onNewSubfolder: (parentId: string) => void
  onMoveItem: (item: DragItem) => void
  onError: (err: unknown) => void
}

function FolderTreeItem({ node, knowledgeBaseId, depth, allFolders, onNewSubfolder, onMoveItem, onError }: FolderTreeItemProps) {
  const [expanded, setExpanded] = useState(false);
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [nonEmptyDeleteError, setNonEmptyDeleteError] = useState<string | null>(null)

  const renameFolder = useRenameFolder(knowledgeBaseId)
  const deleteFolder = useDeleteFolder(knowledgeBaseId)
  const { data: documentsPage } = useDocuments(knowledgeBaseId, expanded ? node.folder.id : null)

  const { attributes, listeners, setNodeRef: setDragRef } = useDraggable({
    id: `folder-${node.folder.id}`,
    data: { type: 'folder', id: node.folder.id } satisfies DragItem,
  })
  const { setNodeRef: setDropRef, isOver } = useDroppable({
    id: `folder-drop-${node.folder.id}`,
    data: { folderId: node.folder.id },
  })

  const closeMenu = () => setAnchorEl(null)

  const handleDelete = (confirm: boolean) => {
    deleteFolder.mutate(
      { folderId: node.folder.id, confirm },
      {
        onError: (err) => {
          const message = err instanceof Error ? err.message : 'Delete failed.'
          if (message.toLowerCase().includes('still contains')) {
            setNonEmptyDeleteError(message)
          } else {
            onError(err)
          }
        },
      },
    )
  }

  return (
    <>
      <ListItem
        disablePadding
        role="treeitem"
        aria-expanded={expanded}
        ref={setDropRef}
        sx={{ pl: depth * 2, bgcolor: isOver ? 'action.hover' : undefined }}
        secondaryAction={
          <IconButton size="small" aria-label="More actions" onClick={(e) => setAnchorEl(e.currentTarget)}>
            <MoreVertIcon fontSize="small" />
          </IconButton>
        }
      >
        <ListItemButton ref={setDragRef} {...attributes} {...listeners} onClick={() => setExpanded((v) => !v)} dense>
          <ListItemIcon sx={{ minWidth: 32 }}>
            {expanded ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
          </ListItemIcon>
          <ListItemIcon sx={{ minWidth: 32 }}>
            <FolderIcon fontSize="small" color="action" />
          </ListItemIcon>
          <ListItemText primary={node.folder.name} />
        </ListItemButton>
      </ListItem>

      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={closeMenu}>
        <MenuItem
          onClick={() => {
            closeMenu()
            onNewSubfolder(node.folder.id)
          }}
        >
          <ListItemIcon>
            <AddIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>New Subfolder</ListItemText>
        </MenuItem>
        <MenuItem
          onClick={() => {
            closeMenu()
            setRenaming(true)
          }}
        >
          <ListItemIcon>
            <EditIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Rename</ListItemText>
        </MenuItem>
        <MenuItem
          onClick={() => {
            closeMenu()
            onMoveItem({ type: 'folder', id: node.folder.id })
          }}
        >
          <ListItemIcon>
            <DriveFileMoveIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Move to… (keyboard-accessible equivalent to drag-and-drop)</ListItemText>
        </MenuItem>
        <MenuItem
          onClick={() => {
            closeMenu()
            handleDelete(false)
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Delete</ListItemText>
        </MenuItem>
      </Menu>

      <Dialog open={renaming} onClose={() => setRenaming(false)}>
        <RenameForm
          initialValue={node.folder.name}
          submitting={renameFolder.isPending}
          onCancel={() => setRenaming(false)}
          onSubmit={(name) =>
            renameFolder.mutate(
              { folderId: node.folder.id, name },
              { onSuccess: () => setRenaming(false), onError },
            )
          }
        />
      </Dialog>

      <ConfirmDialog
        open={nonEmptyDeleteError !== null}
        title="This folder still contains subfolders or documents"
        description={nonEmptyDeleteError ?? ''}
        confirmLabel="Delete anyway"
        onCancel={() => setNonEmptyDeleteError(null)}
        onConfirm={() => {
          setNonEmptyDeleteError(null)
          handleDelete(true)
        }}
      />

      {expanded && (
        <>
          {node.children.map((child) => (
            <FolderTreeItem
              key={child.folder.id}
              node={child}
              knowledgeBaseId={knowledgeBaseId}
              depth={depth + 1}
              allFolders={allFolders}
              onNewSubfolder={onNewSubfolder}
              onMoveItem={onMoveItem}
              onError={onError}
            />
          ))}
          {(documentsPage?.items ?? []).map((doc) => (
            <DocumentRow
              key={doc.id}
              document={doc}
              knowledgeBaseId={knowledgeBaseId}
              depth={depth + 1}
              onMoveItem={onMoveItem}
              onError={onError}
            />
          ))}
        </>
      )}
    </>
  )
}

function DocumentRow({
  document,
  knowledgeBaseId,
  depth = 0,
  onMoveItem,
  onError,
}: {
  document: KnowledgeBaseDocument
  knowledgeBaseId: string
  depth?: number
  onMoveItem: (item: DragItem) => void
  onError: (err: unknown) => void
}) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const deleteDocument = useDeleteDocument(knowledgeBaseId)
  const { attributes, listeners, setNodeRef } = useDraggable({
    id: `document-${document.id}`,
    data: { type: 'document', id: document.id } satisfies DragItem,
  })

  return (
    <ListItem
      data-testid="knowledge-base-document"
      disablePadding
      sx={{ pl: depth * 2 + 2 }}
      secondaryAction={
        <IconButton size="small" aria-label="More actions" onClick={(e) => setAnchorEl(e.currentTarget)}>
          <MoreVertIcon fontSize="small" />
        </IconButton>
      }
    >
      <ListItemButton ref={setNodeRef} {...attributes} {...listeners} dense tabIndex={0}>
        <ListItemIcon sx={{ minWidth: 32 }}>
          <InsertDriveFileIcon fontSize="small" color="action" />
        </ListItemIcon>
        <ListItemText
          primary={document.fileName}
          secondary={
            <span data-testid="document-page-count">
              {document.pageCount !== null ? `${document.pageCount} pages` : 'N/A'}
              {document.processingStatus === 'Failed' && ' · Page count unavailable'}
            </span>
          }
        />
      </ListItemButton>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={() => setAnchorEl(null)}>
        <MenuItem
          onClick={() => {
            setAnchorEl(null)
            onMoveItem({ type: 'document', id: document.id })
          }}
        >
          <ListItemIcon>
            <DriveFileMoveIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Move to…</ListItemText>
        </MenuItem>
        <MenuItem
          onClick={() => {
            setAnchorEl(null)
            deleteDocument.mutate(document.id, { onError })
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Delete</ListItemText>
        </MenuItem>
      </Menu>
    </ListItem>
  )
}

function NewFolderForm({ submitting, onCancel, onSubmit }: { submitting: boolean; onCancel: () => void; onSubmit: (name: string) => void }) {
  const [name, setName] = useState('')
  return (
    <>
      <DialogTitle>New Folder</DialogTitle>
      <DialogContent>
        <TextField
          label="Folder name"
          fullWidth
          autoFocus
          value={name}
          onChange={(e) => setName(e.target.value)}
          sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button variant="contained" disabled={submitting || !name.trim()} onClick={() => onSubmit(name.trim())}>
          Create
        </Button>
      </DialogActions>
    </>
  )
}

function RenameForm({
  initialValue,
  submitting,
  onCancel,
  onSubmit,
}: {
  initialValue: string
  submitting: boolean
  onCancel: () => void
  onSubmit: (name: string) => void
}) {
  const [name, setName] = useState(initialValue)
  return (
    <>
      <DialogTitle>Rename folder</DialogTitle>
      <DialogContent>
        <TextField label="Folder name" fullWidth autoFocus value={name} onChange={(e) => setName(e.target.value)} sx={{ mt: 1 }} />
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button variant="contained" disabled={submitting || !name.trim()} onClick={() => onSubmit(name.trim())}>
          Save
        </Button>
      </DialogActions>
    </>
  )
}

function MoveToPicker({
  open,
  folders,
  onCancel,
  onPick,
}: {
  open: boolean
  folders: KnowledgeBaseFolder[]
  onCancel: () => void
  onPick: (folderId: string | null) => void
}) {
  return (
    <Dialog open={open} onClose={onCancel}>
      <DialogTitle>Move to…</DialogTitle>
      <DialogContent sx={{ p: 0, minWidth: 280 }}>
        <List role="listbox">
          <ListItem disablePadding>
            <ListItemButton role="option" onClick={() => onPick(null)}>
              <ListItemText primary="Knowledge base root" />
            </ListItemButton>
          </ListItem>
          {folders.map((folder) => (
            <ListItem key={folder.id} disablePadding>
              <ListItemButton role="option" onClick={() => onPick(folder.id)}>
                <ListItemText primary={folder.name} />
              </ListItemButton>
            </ListItem>
          ))}
        </List>
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
      </DialogActions>
    </Dialog>
  )
}
