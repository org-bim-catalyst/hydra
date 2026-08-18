import { DndContext, KeyboardSensor, PointerSensor, useDraggable, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import AddIcon from '@mui/icons-material/Add'
import ChevronRightIcon from '@mui/icons-material/ChevronRight'
import DeleteIcon from '@mui/icons-material/Delete'
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove'
import EditIcon from '@mui/icons-material/Edit'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import FolderIcon from '@mui/icons-material/Folder'
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
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { PromptFolder } from '../api/promptFoldersApi'
import { useCreateFolder, useDeleteFolder, useFolderTree, useMoveFolder, useRenameFolder } from '../hooks/usePromptFolders'

interface FolderTreeNode {
  folder: PromptFolder
  children: FolderTreeNode[]
}

function buildFolderTree(folders: PromptFolder[]): FolderTreeNode[] {
  const byId = new Map<string, FolderTreeNode>(folders.map((folder) => [folder.id, { folder, children: [] }]))
  const roots: FolderTreeNode[] = []

  for (const node of byId.values()) {
    if (node.folder.parentFolderId && byId.has(node.folder.parentFolderId)) {
      byId.get(node.folder.parentFolderId)!.children.push(node)
    } else {
      roots.push(node)
    }
  }

  return roots
}

interface FolderTreeProps {
  selectedFolderId: string | null
  onSelectFolder: (folderId: string | null) => void
}

/**
 * Nested folder navigation sidebar (FR-054, spec.md User Story 4). Mouse drag-and-drop moves a
 * folder via `@dnd-kit` (mirrors `KnowledgeBaseFolderTree`); each item's "Move to…" menu action is
 * the keyboard-accessible equivalent, reaching the same `actions/move` endpoint a drop would.
 */
export function FolderTree({ selectedFolderId, onSelectFolder }: FolderTreeProps) {
  const { data: folders = [] } = useFolderTree()
  const createFolder = useCreateFolder()
  const moveFolder = useMoveFolder()

  const [newFolderParentId, setNewFolderParentId] = useState<string | null | undefined>(undefined)
  const [movePickerFolderId, setMovePickerFolderId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const reportError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor),
  )

  const tree = buildFolderTree(folders)

  const handleDragEnd = (event: DragEndEvent) => {
    const draggedFolderId = event.active.data.current?.folderId as string | undefined
    const targetFolderId = (event.over?.data.current as { folderId: string | null } | undefined)?.folderId
    if (!draggedFolderId || targetFolderId === undefined || draggedFolderId === targetFolderId) return

    moveFolder.mutate({ id: draggedFolderId, newParentFolderId: targetFolderId }, { onError: reportError })
  }

  return (
    <Box data-testid="prompt-folder-tree">
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle2">Folders</Typography>
        <Button size="small" startIcon={<AddIcon />} onClick={() => setNewFolderParentId(null)}>
          New Folder
        </Button>
      </Stack>

      <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
        <RootDropZone>
          <List role="tree" aria-label="Prompt folders" dense>
            <RootRow selected={selectedFolderId === null} onClick={() => onSelectFolder(null)} />
            {tree.map((node) => (
              <FolderRow
                key={node.folder.id}
                node={node}
                depth={0}
                selectedFolderId={selectedFolderId}
                onSelectFolder={onSelectFolder}
                onNewSubfolder={(parentId) => setNewFolderParentId(parentId)}
                onMoveFolder={(folderId) => setMovePickerFolderId(folderId)}
                onError={reportError}
              />
            ))}
          </List>
        </RootDropZone>
      </DndContext>

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
        open={movePickerFolderId !== null}
        folders={folders.filter((f) => f.id !== movePickerFolderId)}
        onCancel={() => setMovePickerFolderId(null)}
        onPick={(newParentFolderId) => {
          if (!movePickerFolderId) return
          const id = movePickerFolderId
          setMovePickerFolderId(null)
          moveFolder.mutate({ id, newParentFolderId }, { onError: reportError })
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

function RootDropZone({ children }: { children: React.ReactNode }) {
  const { setNodeRef } = useDroppable({ id: 'root-drop-zone', data: { folderId: null } })
  return <Box ref={setNodeRef}>{children}</Box>
}

function RootRow({ selected, onClick }: { selected: boolean; onClick: () => void }) {
  return (
    <ListItem disablePadding data-testid="prompt-folder-row-root" role="treeitem">
      <ListItemButton selected={selected} onClick={onClick} dense>
        <ListItemIcon sx={{ minWidth: 32 }}>
          <FolderIcon fontSize="small" color="action" />
        </ListItemIcon>
        <ListItemText primary="All prompts" />
      </ListItemButton>
    </ListItem>
  )
}

interface FolderRowProps {
  node: FolderTreeNode
  depth: number
  selectedFolderId: string | null
  onSelectFolder: (folderId: string | null) => void
  onNewSubfolder: (parentId: string) => void
  onMoveFolder: (folderId: string) => void
  onError: (err: unknown) => void
}

function FolderRow({ node, depth, selectedFolderId, onSelectFolder, onNewSubfolder, onMoveFolder, onError }: FolderRowProps) {
  const [expanded, setExpanded] = useState(true)
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [renaming, setRenaming] = useState(false)

  const renameFolder = useRenameFolder()
  const deleteFolder = useDeleteFolder()

  const { attributes, listeners, setNodeRef: setDragRef } = useDraggable({
    id: `folder-${node.folder.id}`,
    data: { folderId: node.folder.id },
  })
  const { setNodeRef: setDropRef, isOver } = useDroppable({
    id: `folder-drop-${node.folder.id}`,
    data: { folderId: node.folder.id },
  })

  const closeMenu = () => setAnchorEl(null)

  return (
    <>
      <ListItem
        disablePadding
        data-testid="prompt-folder-row"
        role="treeitem"
        aria-expanded={node.children.length > 0 ? expanded : undefined}
        ref={setDropRef}
        sx={{ pl: depth * 2, bgcolor: isOver ? 'action.hover' : undefined }}
        secondaryAction={
          <IconButton size="small" aria-label="More actions" onClick={(e) => setAnchorEl(e.currentTarget)}>
            <MoreVertIcon fontSize="small" />
          </IconButton>
        }
      >
        <ListItemButton
          ref={setDragRef}
          {...attributes}
          {...listeners}
          selected={selectedFolderId === node.folder.id}
          onClick={() => onSelectFolder(node.folder.id)}
          dense
        >
          {node.children.length > 0 && (
            <ListItemIcon
              sx={{ minWidth: 24 }}
              onClick={(e) => {
                e.stopPropagation()
                setExpanded((v) => !v)
              }}
            >
              {expanded ? <ExpandMoreIcon fontSize="small" /> : <ChevronRightIcon fontSize="small" />}
            </ListItemIcon>
          )}
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
            onMoveFolder(node.folder.id)
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
            deleteFolder.mutate(node.folder.id, { onError })
            if (selectedFolderId === node.folder.id) onSelectFolder(null)
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
          onSubmit={(name) => renameFolder.mutate({ id: node.folder.id, name }, { onSuccess: () => setRenaming(false), onError })}
        />
      </Dialog>

      {expanded &&
        node.children.map((child) => (
          <FolderRow
            key={child.folder.id}
            node={child}
            depth={depth + 1}
            selectedFolderId={selectedFolderId}
            onSelectFolder={onSelectFolder}
            onNewSubfolder={onNewSubfolder}
            onMoveFolder={onMoveFolder}
            onError={onError}
          />
        ))}
    </>
  )
}

function NewFolderForm({ submitting, onCancel, onSubmit }: { submitting: boolean; onCancel: () => void; onSubmit: (name: string) => void }) {
  const [name, setName] = useState('')
  return (
    <>
      <DialogTitle>New Folder</DialogTitle>
      <DialogContent>
        <TextField label="Folder name" fullWidth autoFocus value={name} onChange={(e) => setName(e.target.value)} sx={{ mt: 1 }} />
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
  folders: PromptFolder[]
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
              <ListItemText primary="Root" />
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
