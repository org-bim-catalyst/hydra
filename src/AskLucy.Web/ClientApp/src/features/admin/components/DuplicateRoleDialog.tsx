import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Snackbar,
  TextField,
} from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleSummary } from '../api/adminRolesApi'

interface DuplicateRoleDialogProps {
  open: boolean
  onClose: () => void
  role: RoleSummary
}

const ROLES_QUERY_KEY = ['admin', 'roles']

/** Suggests "Copy of X", trimmed to the 50-character name limit. */
function suggestedName(roleName: string): string {
  return `Copy of ${roleName}`.slice(0, 50)
}

/**
 * Super User only: saves any role — built-in included — under a new name as a custom role with the
 * same permissions, ready to edit on its own. The server refuses anyone else with a 403.
 */
export function DuplicateRoleDialog({ open, onClose, role }: DuplicateRoleDialogProps) {
  const queryClient = useQueryClient()
  // Mounted fresh per open (the parent renders it conditionally), so these start from the role.
  const [name, setName] = useState(() => suggestedName(role.name))
  const [description, setDescription] = useState(role.description ?? '')
  const [nameError, setNameError] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const duplicateMutation = useMutation({
    mutationFn: () =>
      adminRolesApi.duplicateRole(role.id, { name: name.trim(), description: description.trim() || null }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError: (err: unknown) => {
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.')
    },
  })

  const handleSave = () => {
    const trimmed = name.trim()
    if (trimmed.length < 2 || trimmed.length > 50) {
      setNameError('Role name must be between 2 and 50 characters.')
      return
    }
    setNameError(null)
    duplicateMutation.mutate()
  }

  const permissionCount = role.permissionKeys.length

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
        <DialogTitle>Duplicate {role.name}</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            {permissionCount === 0
              ? `${role.name} has no permissions to copy. Add a permission to it first, or create a new role instead.`
              : `Saves a new custom role with ${role.name}'s ${permissionCount} permission${permissionCount === 1 ? '' : 's'}. No users are moved to it.`}
          </DialogContentText>
          <TextField
            autoFocus
            label="Name"
            fullWidth
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError !== null}
            helperText={nameError}
            slotProps={{ htmlInput: { maxLength: 50 } }}
            sx={{ mb: 2 }}
          />
          <TextField
            label="Description"
            fullWidth
            multiline
            minRows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 250 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button
            onClick={handleSave}
            variant="contained"
            disabled={duplicateMutation.isPending || permissionCount === 0}
          >
            Duplicate
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={errorMessage !== null} autoHideDuration={6000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </>
  )
}
