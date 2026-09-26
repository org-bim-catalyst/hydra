import { useState } from 'react'
import {
  Alert,
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
  Snackbar,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import LockIcon from '@mui/icons-material/Lock'
import LockOpenIcon from '@mui/icons-material/LockOpen'
import SecurityIcon from '@mui/icons-material/Security'
import DeleteIcon from '@mui/icons-material/Delete'
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts'
import KeyIcon from '@mui/icons-material/Key'
import MarkEmailReadIcon from '@mui/icons-material/MarkEmailRead'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { ApiError } from '../../../api/httpClient'
import { ADMIN_ROLES } from '../../../hooks/useIsAdmin'
import * as adminApi from '../api/adminApi'
import type { UserAdmin } from '../api/adminApi'

const USERS_QUERY_KEY = ['admin', 'users']

type PendingAction = 'lock' | 'force2fa' | 'delete' | null

type Feedback = { severity: 'success' | 'error'; message: string } | null

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
  const navigate = useNavigate()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  // A plain Administrator can't touch an Administrator's or Super User's role — any grant/revoke of
  // those requires Super User (FR-014, FR-016). Hide the action entirely for rows a plain
  // Administrator could never change; the server re-checks regardless.
  const canOfferRoleChange = isSuperUser || !ADMIN_ROLES.includes(user.role)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: USERS_QUERY_KEY })

  const lockMutation = useMutation({ mutationFn: () => adminApi.lockUser(user.id), onSuccess: invalidate })
  const unlockMutation = useMutation({ mutationFn: () => adminApi.unlockUser(user.id), onSuccess: invalidate })
  const force2faMutation = useMutation({ mutationFn: () => adminApi.forceReset2fa(user.id), onSuccess: invalidate })
  const deleteMutation = useMutation({ mutationFn: () => adminApi.deleteUser(user.id), onSuccess: invalidate })

  // constitution VIII: a failed request must reach the admin, not just the console.
  const onActionError = (err: unknown) => {
    const message = err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'
    setFeedback({ severity: 'error', message })
  }

  const sendPasswordResetMutation = useMutation({
    mutationFn: () => adminApi.sendPasswordReset(user.id),
    onSuccess: () => setFeedback({ severity: 'success', message: `Password reset link sent to ${user.email}.` }),
    onError: onActionError,
  })

  const resendConfirmationMutation = useMutation({
    mutationFn: () => adminApi.resendConfirmationEmail(user.id),
    onSuccess: () => setFeedback({ severity: 'success', message: `Confirmation email resent to ${user.email}.` }),
    onError: onActionError,
  })

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
              // specs/055-role-management FR-021: links to the Role assignments screen (the
              // single source of role data) rather than duplicating a role picker here.
              navigate(`/admin/role-assignments?search=${encodeURIComponent(user.email)}`)
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
            sendPasswordResetMutation.mutate()
          }}
        >
          <ListItemIcon>
            <KeyIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Send password reset link</ListItemText>
        </MenuItem>
        {!user.emailConfirmed && (
          <MenuItem
            disabled={isSelf}
            onClick={() => {
              closeMenu()
              resendConfirmationMutation.mutate()
            }}
          >
            <ListItemIcon>
              <MarkEmailReadIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Resend confirmation email</ListItemText>
          </MenuItem>
        )}
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

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
