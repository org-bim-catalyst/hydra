import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Snackbar } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import { useT } from '../../../i18n/useT'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleSummary } from '../api/adminRolesApi'

interface DeleteRoleDialogProps {
  open: boolean
  onClose: () => void
  role: RoleSummary
}

const ROLES_QUERY_KEY = ['admin', 'roles']

/** Confirms deletion, naming how many users will be moved to the User role (FR-007/US1-AS5). */
export function DeleteRoleDialog({ open, onClose, role }: DeleteRoleDialogProps) {
  const queryClient = useQueryClient()
  const t = useT('admin.roles')
  const tc = useT('common')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const deleteMutation = useMutation({
    mutationFn: () => adminRolesApi.deleteRole(role.id, role.concurrencyStamp),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError: (err: unknown) => {
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : tc('errors.generic'))
    },
  })

  return (
    <>
      <Dialog open={open} onClose={onClose}>
        <DialogTitle>{t('deleteDialog.title', { name: role.name })}</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {role.userCount > 0
              ? t('deleteDialog.usersMoved', {
                  count: role.userCount,
                  role: adminRolesApi.DEFAULT_ROLE_NAME,
                })
              : t('deleteDialog.noUsers')}{' '}
            {t('deleteDialog.irreversible')}
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('deleteDialog.cancel')}</Button>
          <Button onClick={() => deleteMutation.mutate()} color="error" variant="contained" disabled={deleteMutation.isPending} autoFocus>
            {t('deleteDialog.confirm')}
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
