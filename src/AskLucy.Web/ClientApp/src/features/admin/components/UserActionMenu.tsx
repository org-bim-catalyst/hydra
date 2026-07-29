import { useState } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Select,
  Typography,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import LockIcon from '@mui/icons-material/Lock'
import LockOpenIcon from '@mui/icons-material/LockOpen'
import SecurityIcon from '@mui/icons-material/Security'
import DeleteIcon from '@mui/icons-material/Delete'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as adminApi from '../api/adminApi'
import type { UserAdmin, UserRole } from '../api/adminApi'

const USERS_QUERY_KEY = ['admin', 'users']

type PendingAction = 'lock' | 'force2fa' | 'delete' | null

interface UserActionMenuProps {
  user: UserAdmin
  isSelf: boolean
  /** Whether the acting admin holds the Super User role — gates granting/revoking Administrator/Super User (FR-014). */
  isSuperUser: boolean
}

const CONFIRM_COPY: Record<Exclude<PendingAction, null>, { title: string; body: string }> = {
  lock: { title: 'Lock this account?', body: 'The user will no longer be able to sign in until unlocked.' },
  force2fa: {
    title: 'Force a 2FA reset?',
    body: "The user's existing authenticator enrollment will be cleared; they'll need to re-enroll.",
  },
  delete: {
    title: 'Delete this account?',
    body: 'The account will be deactivated and can no longer sign in. This cannot be undone from this screen.',
  },
}

/** Lock/unlock/role-change/force-2FA-reset/delete row actions (specs/001-admin-dashboard FR-012 through FR-017). */
export function UserActionMenu({ user, isSelf, isSuperUser }: UserActionMenuProps) {
  const queryClient = useQueryClient()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [roleDialogOpen, setRoleDialogOpen] = useState(false)
  const [selectedRole, setSelectedRole] = useState<UserRole>(user.role)

  // A plain Administrator can only touch a currently-Regular user's role (and only ever
  // set it to Regular) — any grant/revoke of Administrator/Super User requires Super User
  // (FR-014). Hide the action entirely for rows a plain Administrator could never change.
  const canOfferRoleChange = isSuperUser || user.role === 'Regular'

  const invalidate = () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY })

  const lockMutation = useMutation({ mutationFn: () => adminApi.lockUser(user.id), onSuccess: invalidate })
  const unlockMutation = useMutation({ mutationFn: () => adminApi.unlockUser(user.id), onSuccess: invalidate })
  const roleMutation = useMutation({
    mutationFn: (role: UserRole) => adminApi.changeUserRole(user.id, role),
    onSuccess: invalidate,
  })
  const force2faMutation = useMutation({ mutationFn: () => adminApi.forceReset2fa(user.id), onSuccess: invalidate })
  const deleteMutation = useMutation({ mutationFn: () => adminApi.deleteUser(user.id), onSuccess: invalidate })

  const closeMenu = () => setAnchorEl(null)

  const handleConfirm = () => {
    if (pendingAction === 'lock') lockMutation.mutate()
    if (pendingAction === 'force2fa') force2faMutation.mutate()
    if (pendingAction === 'delete') deleteMutation.mutate()
    setPendingAction(null)
  }

  return (
    <>
      <IconButton size="small" aria-label={`Actions for ${user.email}`} onClick={(e) => setAnchorEl(e.currentTarget)}>
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={closeMenu}>
        {user.isLockedOut ? (
          <MenuItem
            disabled={isSelf}
            onClick={() => {
              closeMenu()
              unlockMutation.mutate()
            }}
          >
            <ListItemIcon>
              <LockOpenIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Unlock account</ListItemText>
          </MenuItem>
        ) : (
          <MenuItem
            disabled={isSelf}
            onClick={() => {
              closeMenu()
              setPendingAction('lock')
            }}
          >
            <ListItemIcon>
              <LockIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Lock account</ListItemText>
          </MenuItem>
        )}
        {canOfferRoleChange && (
          <MenuItem
            onClick={() => {
              closeMenu()
              setSelectedRole(user.role)
              setRoleDialogOpen(true)
            }}
          >
            <ListItemIcon>
              <ManageAccountsIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Change role&hellip;</ListItemText>
          </MenuItem>
        )}
        <MenuItem
          disabled={isSelf}
          onClick={() => {
            closeMenu()
            setPendingAction('force2fa')
          }}
        >
          <ListItemIcon>
            <SecurityIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Force 2FA reset</ListItemText>
        </MenuItem>
        <MenuItem
          disabled={isSelf}
          onClick={() => {
            closeMenu()
            setPendingAction('delete')
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" color={isSelf ? undefined : 'error'} />
          </ListItemIcon>
          <ListItemText>Delete account</ListItemText>
        </MenuItem>
      </Menu>

      <Dialog open={pendingAction !== null} onClose={() => setPendingAction(null)}>
        {pendingAction && (
          <>
            <DialogTitle>{CONFIRM_COPY[pendingAction].title}</DialogTitle>
            <DialogContent>
              <DialogContentText>{CONFIRM_COPY[pendingAction].body}</DialogContentText>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setPendingAction(null)}>Cancel</Button>
              <Button onClick={handleConfirm} color="error" variant="contained" autoFocus>
                Confirm
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>

      <Dialog open={roleDialogOpen} onClose={() => setRoleDialogOpen(false)}>
        <DialogTitle>Change role for {user.email}</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Only a Super User can grant or revoke the Administrator or Super User role.
          </Typography>
          <Select
            fullWidth
            value={selectedRole}
            onChange={(e) => setSelectedRole(e.target.value as UserRole)}
            size="small"
          >
            <MenuItem value="Regular">Regular</MenuItem>
            <MenuItem value="Administrator" disabled={!isSuperUser}>
              Administrator
            </MenuItem>
            <MenuItem value="Super User" disabled={!isSuperUser}>
              Super User
            </MenuItem>
          </Select>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRoleDialogOpen(false)}>Cancel</Button>
          <Button
            variant="contained"
            onClick={() => {
              roleMutation.mutate(selectedRole)
              setRoleDialogOpen(false)
            }}
          >
            Save
          </Button>
        </DialogActions>
      </Dialog>
    </>
  )
}
