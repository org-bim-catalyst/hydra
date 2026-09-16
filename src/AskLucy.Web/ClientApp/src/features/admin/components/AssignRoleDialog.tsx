import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  MenuItem,
  Snackbar,
  TextField,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleAssignment } from '../api/adminRolesApi'

interface AssignRoleDialogProps {
  open: boolean
  onClose: () => void
  assignment: RoleAssignment
}

const NO_ROLE_VALUE = ''
const ASSIGNMENTS_QUERY_KEY = ['admin', 'role-assignments']

/** Assign/change/remove one user's role (US2) — the server enforces the privileged-role rule and the last-Super-User safeguard; this dialog just surfaces whatever it says. */
export function AssignRoleDialog({ open, onClose, assignment }: AssignRoleDialogProps) {
  const queryClient = useQueryClient()
  // The parent conditionally renders this dialog (`editingAssignment && <AssignRoleDialog .../>`)
  // rather than toggling `open`, so a fresh mount — not an effect — is what resets these on
  // every open/close cycle.
  const [selectedRoleId, setSelectedRoleId] = useState(assignment.role?.id ?? NO_ROLE_VALUE)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const { data: roles } = useQuery({
    queryKey: ['admin', 'roles', 'all-for-picker'],
    queryFn: () => adminRolesApi.getRoles({ pageSize: 100 }),
    enabled: open,
  })

  const assignMutation = useMutation({
    mutationFn: () =>
      adminRolesApi.assignRole(assignment.userId, selectedRoleId === NO_ROLE_VALUE ? null : selectedRoleId, assignment.role?.id ?? null),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ASSIGNMENTS_QUERY_KEY })
      onClose()
    },
    onError: (err: unknown) => {
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.')
    },
  })

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="xs" fullWidth>
        <DialogTitle>Change role for {assignment.email}</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            Assigning a role replaces this user's current role — a user holds at most one role.
          </DialogContentText>
          <TextField
            select
            label="Role"
            fullWidth
            value={selectedRoleId}
            onChange={(e) => setSelectedRoleId(e.target.value)}
          >
            <MenuItem value={NO_ROLE_VALUE}>No role</MenuItem>
            {roles?.items.map((role) => (
              <MenuItem key={role.id} value={role.id}>
                {role.name}
                {role.isBuiltIn ? ' (built-in)' : ''}
              </MenuItem>
            ))}
          </TextField>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button onClick={() => assignMutation.mutate()} variant="contained" disabled={assignMutation.isPending}>
            Save
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
