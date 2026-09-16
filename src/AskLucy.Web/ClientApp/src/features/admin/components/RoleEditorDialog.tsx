import { useEffect, useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Snackbar,
  TextField,
} from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleSummary } from '../api/adminRolesApi'
import { PermissionPicker } from './PermissionPicker'

interface RoleEditorDialogProps {
  open: boolean
  onClose: () => void
  /** Present when editing; absent when creating. */
  role?: RoleSummary
}

const ROLES_QUERY_KEY = ['admin', 'roles']

/** Create/edit form for a custom role (US1) — mirrors CreateRoleCommandValidator/UpdateRoleCommandValidator's rules for immediate inline feedback. */
export function RoleEditorDialog({ open, onClose, role }: RoleEditorDialogProps) {
  const queryClient = useQueryClient()
  const isEdit = role !== undefined

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [permissionKeys, setPermissionKeys] = useState<string[]>([])
  const [nameError, setNameError] = useState<string | null>(null)
  const [permissionError, setPermissionError] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setName(role?.name ?? '')
      setDescription(role?.description ?? '')
      setPermissionKeys(role?.permissionKeys ?? [])
      setNameError(null)
      setPermissionError(null)
      setErrorMessage(null)
    }
  }, [open, role])

  const onError = (err: unknown) => {
    setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.')
  }

  const createMutation = useMutation({
    mutationFn: () => adminRolesApi.createRole({ name: name.trim(), description: description.trim() || null, permissionKeys }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError,
  })

  const updateMutation = useMutation({
    mutationFn: () =>
      adminRolesApi.updateRole(role!.id, {
        name: name.trim(),
        description: description.trim() || null,
        permissionKeys,
        concurrencyStamp: role!.concurrencyStamp,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError,
  })

  const isPending = createMutation.isPending || updateMutation.isPending

  const validate = (): boolean => {
    const trimmed = name.trim()
    let valid = true

    if (trimmed.length < 2 || trimmed.length > 50) {
      setNameError('Role name must be between 2 and 50 characters.')
      valid = false
    } else {
      setNameError(null)
    }

    if (permissionKeys.length === 0) {
      setPermissionError('Select at least one permission.')
      valid = false
    } else {
      setPermissionError(null)
    }

    return valid
  }

  const handleSave = () => {
    if (!validate()) return
    if (isEdit) updateMutation.mutate()
    else createMutation.mutate()
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
        <DialogTitle>{isEdit ? `Edit ${role.name}` : 'Create role'}</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            label="Name"
            fullWidth
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError !== null}
            helperText={nameError}
            sx={{ mt: 1, mb: 2 }}
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
          <PermissionPicker selectedKeys={permissionKeys} onChange={setPermissionKeys} />
          {permissionError && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {permissionError}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button onClick={handleSave} variant="contained" disabled={isPending}>
            {isEdit ? 'Save' : 'Create'}
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
