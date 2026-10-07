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
import { useT } from '../../../i18n/useT'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleSummary } from '../api/adminRolesApi'

interface DuplicateRoleDialogProps {
  open: boolean
  onClose: () => void
  role: RoleSummary
}

const ROLES_QUERY_KEY = ['admin', 'roles']

/**
 * Suggests "Copy of X", trimmed to the 50-character name limit. The name is the field's value, so it is built here
 * from the translated prefix, never through a message param (which would add direction isolates to the text).
 */
function suggestedName(prefix: string, roleName: string): string {
  return `${prefix} ${roleName}`.slice(0, 50)
}

/**
 * Super User only: saves any role — built-in included — under a new name as a custom role with the
 * same permissions, ready to edit on its own. The server refuses anyone else with a 403.
 */
export function DuplicateRoleDialog({ open, onClose, role }: DuplicateRoleDialogProps) {
  const queryClient = useQueryClient()
  const t = useT('admin.roles')
  const tc = useT('common')
  // Mounted fresh per open (the parent renders it conditionally), so these start from the role.
  const [name, setName] = useState(() => suggestedName(t('duplicateDialog.copyPrefix'), role.name))
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
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : tc('errors.generic'))
    },
  })

  const handleSave = () => {
    const trimmed = name.trim()
    if (trimmed.length < 2 || trimmed.length > 50) {
      setNameError(t('duplicateDialog.nameLength'))
      return
    }
    setNameError(null)
    duplicateMutation.mutate()
  }

  const permissionCount = role.permissionKeys.length

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
        <DialogTitle>{t('duplicateDialog.title', { name: role.name })}</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            {permissionCount === 0
              ? t('duplicateDialog.noPermissions', { name: role.name })
              : t('duplicateDialog.summary', { name: role.name, count: permissionCount })}
          </DialogContentText>
          <TextField
            autoFocus
            label={t('duplicateDialog.name')}
            fullWidth
            value={name}
            onChange={(e) => setName(e.target.value)}
            error={nameError !== null}
            helperText={nameError}
            slotProps={{ htmlInput: { maxLength: 50 } }}
            sx={{ mb: 2 }}
          />
          <TextField
            label={t('duplicateDialog.description')}
            fullWidth
            multiline
            minRows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 250 } }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('duplicateDialog.cancel')}</Button>
          <Button
            onClick={handleSave}
            variant="contained"
            disabled={duplicateMutation.isPending || permissionCount === 0}
          >
            {t('duplicateDialog.confirm')}
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
