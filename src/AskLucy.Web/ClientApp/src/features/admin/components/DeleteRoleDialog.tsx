import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Snackbar } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
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
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const deleteMutation = useMutation({
    mutationFn: () => adminRolesApi.deleteRole(role.id, role.concurrencyStamp),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError: (err: unknown) => {
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.')
    },
  })

  return (
    <>
      <Dialog open={open} onClose={onClose}>
        <DialogTitle>Delete {role.name}?</DialogTitle>
        <DialogContent>
          <DialogContentText>
            {role.userCount > 0
              ? `${role.userCount} user${role.userCount === 1 ? '' : 's'} currently hold${role.userCount === 1 ? 's' : ''} this role and will be moved to the ${adminRolesApi.DEFAULT_ROLE_NAME} role.`
              : 'No users currently hold this role.'}{' '}
            This cannot be undone.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button onClick={() => deleteMutation.mutate()} color="error" variant="contained" disabled={deleteMutation.isPending} autoFocus>
            Delete
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
