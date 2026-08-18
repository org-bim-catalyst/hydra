import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import FolderIcon from '@mui/icons-material/Folder'
import {
  Alert,
  Box,
  Button,
  IconButton,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Snackbar,
  Stack,
  TextField,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { EmptyState } from '../../../components/EmptyState'
import type { Project } from '../api/projectsApi'
import { useCreateProject, useDeleteProject, useRenameProject } from '../hooks/useProjectMutations'
import { useProjects } from '../hooks/useProjects'

/**
 * spec.md FR-002a, User Story 5 — create/rename/delete Projects. Deleting archives (never
 * immediately deletes) the Project's scoped memories server-side (User Story 5 AC3); this panel
 * only surfaces the confirmation and the resulting list change.
 */
export function ProjectManagementPanel() {
  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } = useProjects()
  const createProject = useCreateProject()
  const renameProject = useRenameProject()
  const deleteProject = useDeleteProject()

  const [newName, setNewName] = useState('')
  const [renameTarget, setRenameTarget] = useState<Project | null>(null)
  const [renameValue, setRenameValue] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<Project | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const projects = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])
  const reportError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  const handleCreate = () => {
    const name = newName.trim()
    if (!name) return
    createProject.mutate(name, { onSuccess: () => setNewName(''), onError: reportError })
  }

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
        <TextField
          size="small"
          fullWidth
          placeholder="New project name"
          aria-label="New project name"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              handleCreate()
            }
          }}
        />
        <Button variant="contained" startIcon={<AddIcon />} onClick={handleCreate} disabled={createProject.isPending}>
          Create
        </Button>
      </Stack>

      {!isLoading && projects.length === 0 && (
        <EmptyState icon={<FolderIcon fontSize="inherit" />} title="No Projects yet" description="Group related conversations together to keep their memories scoped." />
      )}

      <List disablePadding>
        {projects.map((project) => (
          <ListItem
            key={project.id}
            secondaryAction={
              <Stack direction="row">
                <IconButton
                  size="small"
                  aria-label="Rename project"
                  onClick={() => {
                    setRenameTarget(project)
                    setRenameValue(project.name)
                  }}
                >
                  <EditIcon fontSize="small" />
                </IconButton>
                <IconButton size="small" aria-label="Delete project" onClick={() => setDeleteTarget(project)}>
                  <DeleteIcon fontSize="small" />
                </IconButton>
              </Stack>
            }
          >
            <ListItemIcon>
              <FolderIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary={project.name} />
          </ListItem>
        ))}
      </List>

      {hasNextPage && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 2 }}>
          <Button variant="outlined" onClick={() => fetchNextPage()} loading={isFetchingNextPage}>
            Load more
          </Button>
        </Box>
      )}

      {renameTarget && (
        <Stack direction="row" spacing={1} sx={{ mt: 2, alignItems: 'center' }}>
          <TextField
            size="small"
            fullWidth
            aria-label="Rename project"
            value={renameValue}
            onChange={(e) => setRenameValue(e.target.value)}
            autoFocus
          />
          <Button
            variant="contained"
            disabled={renameProject.isPending}
            onClick={() => {
              const name = renameValue.trim()
              if (!name) return
              renameProject.mutate({ id: renameTarget.id, name }, { onSuccess: () => setRenameTarget(null), onError: reportError })
            }}
          >
            Save
          </Button>
          <Button onClick={() => setRenameTarget(null)}>Cancel</Button>
        </Stack>
      )}

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete this Project?"
        description="Its scoped memories will be archived, not deleted, and its conversations keep their history. This cannot be undone."
        confirmLabel="Delete"
        onCancel={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (!deleteTarget) return
          const id = deleteTarget.id
          setDeleteTarget(null)
          deleteProject.mutate(id, { onError: reportError })
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
