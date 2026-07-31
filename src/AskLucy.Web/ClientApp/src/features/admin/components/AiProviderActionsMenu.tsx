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
  TextField,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import KeyIcon from '@mui/icons-material/Key'
import PowerSettingsNewIcon from '@mui/icons-material/PowerSettingsNew'
import DeleteIcon from '@mui/icons-material/Delete'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

interface AiProviderActionsMenuProps {
  provider: AdminAiProvider
}

type PendingAction = 'enable' | 'disable' | 'clearCredential' | null

type Feedback = { severity: 'success' | 'error'; message: string } | null

const CONFIRM_COPY: Record<Exclude<PendingAction, null>, { title: string; body: string }> = {
  enable: {
    title: 'Enable this provider?',
    body: 'End users will be able to select it as soon as you confirm.',
  },
  disable: {
    title: 'Disable this provider?',
    body: 'End users will no longer be able to select it. Conversations that already used it keep their history.',
  },
  clearCredential: {
    title: 'Clear this credential?',
    body: 'This will also disable the provider — a provider can never stay enabled with no credential configured.',
  },
}

/**
 * specs/007-admin-ai-provider-ui — enable/disable/set-credential/clear-credential actions
 * for one AI provider row, each confirm-gated (FR-010). Mirrors UserActionMenu.tsx's
 * menu + confirm-dialog composition.
 */
export function AiProviderActionsMenu({ provider }: AiProviderActionsMenuProps) {
  const queryClient = useQueryClient()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [credentialDialogOpen, setCredentialDialogOpen] = useState(false)
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [feedback, setFeedback] = useState<Feedback>(null)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY })

  const onError = (err: unknown) => {
    const message = err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'
    setFeedback({ severity: 'error', message })
  }

  const updateMutation = useMutation({
    mutationFn: (isEnabled: boolean) => adminAiProvidersApi.updateProvider(provider.id, { isEnabled }),
    onSuccess: (_, isEnabled) => {
      invalidate()
      setFeedback({ severity: 'success', message: isEnabled ? 'Provider enabled.' : 'Provider disabled.' })
    },
    onError,
  })

  const setCredentialMutation = useMutation({
    mutationFn: (apiKey: string) => adminAiProvidersApi.setCredential(provider.id, apiKey),
    onSuccess: () => {
      invalidate()
      setFeedback({ severity: 'success', message: 'Credential saved.' })
    },
    onError,
  })

  const clearCredentialMutation = useMutation({
    mutationFn: () => adminAiProvidersApi.clearCredential(provider.id),
    onSuccess: () => {
      invalidate()
      setFeedback({ severity: 'success', message: 'Credential cleared and provider disabled.' })
    },
    onError,
  })

  const closeMenu = () => setAnchorEl(null)

  const handleEnableDisableClick = () => {
    closeMenu()
    if (!provider.isEnabled && !provider.hasCredential) {
      // FR-003: already known client-side from the fetched row — no API call, no
      // confirmation dialog, just the explanation immediately.
      setFeedback({ severity: 'error', message: 'This provider needs a credential before it can be enabled.' })
      return
    }
    setPendingAction(provider.isEnabled ? 'disable' : 'enable')
  }

  const handleConfirm = () => {
    if (pendingAction === 'enable') updateMutation.mutate(true)
    if (pendingAction === 'disable') updateMutation.mutate(false)
    if (pendingAction === 'clearCredential') clearCredentialMutation.mutate()
    setPendingAction(null)
  }

  const openCredentialDialog = () => {
    closeMenu()
    setApiKeyInput('')
    setCredentialDialogOpen(true)
  }

  const closeCredentialDialog = () => {
    setCredentialDialogOpen(false)
    setApiKeyInput('')
  }

  const handleCredentialConfirm = () => {
    if (!apiKeyInput.trim()) {
      setFeedback({ severity: 'error', message: 'An API key is required.' })
      return
    }
    setCredentialMutation.mutate(apiKeyInput);
    closeCredentialDialog()
  }

  return (
    <>
      <IconButton size="small" aria-label={`Actions for ${provider.displayName}`} onClick={(e) => setAnchorEl(e.currentTarget)}>
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={closeMenu}>
        <MenuItem onClick={openCredentialDialog}>
          <ListItemIcon>
            <KeyIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>{provider.hasCredential ? 'Replace credential' : 'Set credential'}</ListItemText>
        </MenuItem>
        <MenuItem onClick={handleEnableDisableClick}>
          <ListItemIcon>
            <PowerSettingsNewIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>{provider.isEnabled ? 'Disable' : 'Enable'}</ListItemText>
        </MenuItem>
        <MenuItem
          disabled={!provider.hasCredential}
          onClick={() => {
            closeMenu()
            setPendingAction('clearCredential')
          }}
        >
          <ListItemIcon>
            <DeleteIcon fontSize="small" color={provider.hasCredential ? 'error' : undefined} />
          </ListItemIcon>
          <ListItemText>Clear credential</ListItemText>
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
              <Button onClick={handleConfirm} color={pendingAction === 'enable' ? 'primary' : 'error'} variant="contained" autoFocus>
                Confirm
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>

      <Dialog open={credentialDialogOpen} onClose={closeCredentialDialog}>
        <DialogTitle>
          {provider.hasCredential ? 'Replace credential for' : 'Set credential for'} {provider.displayName}
        </DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            The value is never shown again once saved.
          </DialogContentText>
          <TextField
            label="API key"
            type="password"
            fullWidth
            autoFocus
            value={apiKeyInput}
            onChange={(e) => setApiKeyInput(e.target.value)}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={closeCredentialDialog}>Cancel</Button>
          <Button onClick={handleCredentialConfirm} variant="contained">
            Confirm
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
