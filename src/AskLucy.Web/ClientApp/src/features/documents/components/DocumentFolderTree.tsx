import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove'
import FolderIcon from '@mui/icons-material/Folder'
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
  MenuItem,
  Stack,
  TextField,
  Tooltip,
} from '@mui/material'
import { useState } from 'react'
import type { DocumentFolder, OnContainedDocumentsAction } from '../api/documentsApi'
import { useFolderTree } from '../hooks/useDocuments'
import { useCreateFolder, useDeleteFolder } from '../hooks/useDocumentMutations'

interface DocumentFolderTreeProps {
  selectedFolderId: string | null
  onSelectFolder: (folderId: string | null) => void
}

/** FR-033 — folder navigation (create, delete with explicit contained-document handling, select-to-filter). Rename/move are available via each row's own detail actions in a future pass; this component covers the tree's core navigation and lifecycle. */
export function DocumentFolderTree({ selectedFolderId, onSelectFolder }: DocumentFolderTreeProps) {
  const { data: folders, isLoading, isError } = useFolderTree()
  const createFolder = useCreateFolder()
  const deleteFolder = useDeleteFolder()

  const [creating, setCreating] = useState(false)
  const [newFolderName, setNewFolderName] = useState('')
  const [pendingDelete, setPendingDelete] = useState<DocumentFolder | null>(null)
  const [onContainedDocuments, setOnContainedDocuments] = useState<OnContainedDocumentsAction>('MoveToParent')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleCreate = () => {
    const name = newFolderName.trim()
    if (!name) return
    createFolder.mutate(
      { name, parentFolderId: null },
      { onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not create the folder.') },
    )
    setNewFolderName('')
    setCreating(false)
  }

  const handleConfirmDelete = () => {
    if (!pendingDelete) return
    deleteFolder.mutate(
      { id: pendingDelete.id, onContainedDocuments: pendingDelete.documentCount > 0 ? onContainedDocuments : undefined },
      {
        onSuccess: () => {
          if (selectedFolderId === pendingDelete.id) onSelectFolder(null)
        },
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not delete the folder.'),
      },
    )
    setPendingDelete(null)
  }

  if (isError) {
    return <Alert severity="error">Could not load folders.</Alert>
  }

  return (
    <Box>
      {errorMessage && (
        <Alert severity="error" sx={{ mb: 1 }} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <List dense disablePadding>
        <ListItem disablePadding>
          <ListItemButton selected={selectedFolderId === null} onClick={() => onSelectFolder(null)}>
            <ListItemIcon>
              <FolderIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary="All documents" />
          </ListItemButton>
        </ListItem>

        {!isLoading &&
          folders?.map((folder) => (
            <ListItem
              key={folder.id}
              disablePadding
              secondaryAction={
                <Tooltip title="Delete folder">
                  <IconButton
                    size="small"
                    edge="end"
                    aria-label={`Delete ${folder.name}`}
                    onClick={() => setPendingDelete(folder)}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              }
            >
              <ListItemButton
                selected={selectedFolderId === folder.id}
                onClick={() => onSelectFolder(folder.id)}
                sx={{ pl: 2 + folder.depth * 2 }}
              >
                <ListItemIcon>
                  <FolderIcon fontSize="small" />
                </ListItemIcon>
                <ListItemText primary={`${folder.name} (${folder.documentCount})`} />
              </ListItemButton>
            </ListItem>
          ))}
      </List>

      {creating ? (
        <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
          <TextField
            size="small"
            autoFocus
            placeholder="Folder name"
            value={newFolderName}
            onChange={(e) => setNewFolderName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
          />
          <Button size="small" onClick={handleCreate}>
            Create
          </Button>
        </Stack>
      ) : (
        <Button size="small" startIcon={<AddIcon fontSize="small" />} onClick={() => setCreating(true)} sx={{ mt: 1 }}>
          New folder
        </Button>
      )}

      <Dialog open={pendingDelete !== null} onClose={() => setPendingDelete(null)}>
        <DialogTitle>Delete "{pendingDelete?.name}"?</DialogTitle>
        <DialogContent>
          {pendingDelete && pendingDelete.documentCount > 0 && (
            <TextField
              select
              fullWidth
              label="What should happen to the documents in this folder?"
              value={onContainedDocuments}
              onChange={(e) => setOnContainedDocuments(e.target.value as OnContainedDocumentsAction)}
              sx={{ mt: 1 }}
            >
              <MenuItem value="MoveToParent">Move to parent folder</MenuItem>
              <MenuItem value="ArchiveAll">Archive all</MenuItem>
              <MenuItem value="DeleteAll">Delete all</MenuItem>
            </TextField>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setPendingDelete(null)}>Cancel</Button>
          <Button color="error" onClick={handleConfirmDelete} startIcon={<DriveFileMoveIcon fontSize="small" />}>
            Delete
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
