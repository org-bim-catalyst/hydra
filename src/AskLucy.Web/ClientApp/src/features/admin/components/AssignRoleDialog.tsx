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
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { isSuperUserControlledRole } from '../adminPermissions'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleAssignment } from '../api/adminRolesApi'

interface AssignRoleDialogProps {
  open: boolean
  onClose: () => void
  assignment: RoleAssignment
}

const ASSIGNMENTS_QUERY_KEY = ['admin', 'role-assignments']

/**
 * Assign/change one user's role (US2) — the server enforces the privileged-role rule and the
 * last-Super-User safeguard; this dialog just surfaces whatever it says. There's no "no role": a user
 * always holds one, the User role at the least.
 */
export function AssignRoleDialog({ open, onClose, assignment }: AssignRoleDialogProps) {
  const queryClient = useQueryClient()
  // The parent conditionally renders this dialog (`editingAssignment && <AssignRoleDialog .../>`)
  // rather than toggling `open`, so a fresh mount — not an effect — is what resets these on
  // every open/close cycle.
  const [pickedRoleId, setPickedRoleId] = useState(assignment.role?.id ?? '')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const { data: roles } = useQuery({
    queryKey: ['admin', 'roles', 'all-for-picker'],
    queryFn: () => adminRolesApi.getRoles({ pageSize: 100 }),
    enabled: open,
  })

  // specs/074 FR-016f: a role carrying View user content is Super-User-only to give or take away.
  // UX only — the server refuses the change with a 403 regardless.
  const isSuperUser = useIsSuperUser()
  const isLockedRole = (roleId: string | undefined) => {
    const role = roles?.items.find((r) => r.id === roleId)
    return !isSuperUser && role !== undefined && isSuperUserControlledRole(role)
  }
  const currentRoleLocked = isLockedRole(assignment.role?.id)
  // An account from before the User role existed has no role row, and is a User-role holder.
  const selectedRoleId = pickedRoleId || (roles?.items.find(adminRolesApi.isDefaultRole)?.id ?? '')

  const assignMutation = useMutation({
    mutationFn: () =>
      adminRolesApi.assignRole(assignment.userId, selectedRoleId, assignment.role?.id ?? null),
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
          {currentRoleLocked && (
            <Alert severity="info" sx={{ mb: 2 }}>
              This user's role includes View user content. Only a Super User can change it.
            </Alert>
          )}
          <TextField
            select
            label="Role"
            fullWidth
            value={selectedRoleId}
            disabled={currentRoleLocked}
            onChange={(e) => setPickedRoleId(e.target.value)}
          >
            {roles?.items.map((role) => (
              <MenuItem key={role.id} value={role.id} disabled={isLockedRole(role.id)}>
                {role.name}
                {role.isBuiltIn ? ' (built-in)' : ''}
                {isLockedRole(role.id) ? ' (Super User only)' : ''}
              </MenuItem>
            ))}
          </TextField>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button onClick={() => assignMutation.mutate()} variant="contained" disabled={assignMutation.isPending || currentRoleLocked || !selectedRoleId}>
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
