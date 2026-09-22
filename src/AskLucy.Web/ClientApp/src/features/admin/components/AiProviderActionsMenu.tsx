import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  InputAdornment,
  Snackbar,
  TextField,
  Tooltip,
} from '@mui/material'
import KeyIcon from '@mui/icons-material/Key'
import PowerSettingsNewIcon from '@mui/icons-material/PowerSettingsNew'
import DeleteIcon from '@mui/icons-material/Delete'
import HealthAndSafetyIcon from '@mui/icons-material/HealthAndSafety'
import VisibilityIcon from '@mui/icons-material/Visibility'
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff'
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
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [credentialDialogOpen, setCredentialDialogOpen] = useState(false)
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [showApiKey, setShowApiKey] = useState(false)
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

  /**
   * specs/043 US3 (FR-024) — turns diagnosis into a closed loop: after replacing a credential
   * or enabling billing, an administrator can confirm the fix now instead of waiting out the
   * background cycle. FR-025's concurrency bound is the controller's existing
   * `admin-endpoints` rate limit plus the pending-disabled trigger below, not new machinery.
   */
  const checkHealthMutation = useMutation({
    mutationFn: () => adminAiProvidersApi.checkProviderHealth(provider.id),
    onSuccess: (result) => {
      invalidate()
      setFeedback(
        result.healthStatus === 'Healthy'
          ? { severity: 'success', message: `${provider.displayName} is healthy.` }
          : {
              severity: 'error',
              // The server's own classified prose — already administrator-facing and free of
              // any vendor body or credential (FR-013).
              message: result.healthFailureReason ?? `${provider.displayName} is unhealthy.`,
            },
      )
    },
    // constitution VIII: a failed probe request must reach the user, not just the console.
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

  const handleEnableDisableClick = () => {
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
    setApiKeyInput('')
    setShowApiKey(false)
    setCredentialDialogOpen(true)
  }

  const closeCredentialDialog = () => {
    setCredentialDialogOpen(false)
    setApiKeyInput('')
    setShowApiKey(false)
  }

  const handleCredentialConfirm = () => {
    if (!apiKeyInput.trim()) {
      setFeedback({ severity: 'error', message: 'An API key is required.' })
      return
    }
    setCredentialMutation.mutate(apiKeyInput);
    closeCredentialDialog()
  }

  const credentialLabel = provider.hasCredential ? 'Replace credential' : 'Set credential'
  const enableDisableLabel = provider.isEnabled ? 'Disable' : 'Enable'
  const checkNowLabel = checkHealthMutation.isPending ? 'Checking…' : 'Check now'

  return (
    <>
      <Box sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
        <Tooltip title={credentialLabel}>
          <IconButton size="small" aria-label={`${credentialLabel} for ${provider.displayName}`} onClick={openCredentialDialog}>
            <KeyIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title={enableDisableLabel}>
          <IconButton
            size="small"
            aria-label={`${enableDisableLabel} ${provider.displayName}`}
            onClick={handleEnableDisableClick}
          >
            <PowerSettingsNewIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title={checkNowLabel}>
          <span>
            <IconButton
              size="small"
              aria-label={`Check now for ${provider.displayName}`}
              disabled={!provider.hasCredential || checkHealthMutation.isPending}
              onClick={() => checkHealthMutation.mutate()}
            >
              <HealthAndSafetyIcon fontSize="small" />
            </IconButton>
          </span>
        </Tooltip>
        <Tooltip title="Clear credential">
          <span>
            <IconButton
              size="small"
              aria-label={`Clear credential for ${provider.displayName}`}
              disabled={!provider.hasCredential}
              onClick={() => setPendingAction('clearCredential')}
            >
              <DeleteIcon fontSize="small" color={provider.hasCredential ? 'error' : undefined} />
            </IconButton>
          </span>
        </Tooltip>
      </Box>

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

      <Dialog open={credentialDialogOpen} onClose={closeCredentialDialog} maxWidth="sm" fullWidth>
        <DialogTitle>
          {provider.hasCredential ? 'Replace credential for' : 'Set credential for'} {provider.displayName}
        </DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            The value is never shown again once saved.
          </DialogContentText>
          <TextField
            label="API key"
            type={showApiKey ? 'text' : 'password'}
            placeholder="Please insert API key here"
            fullWidth
            autoFocus
            value={apiKeyInput}
            onChange={(e) => setApiKeyInput(e.target.value)}
            slotProps={{
              input: {
                endAdornment: apiKeyInput.length > 0 && (
                  <InputAdornment position="end">
                    <IconButton
                      aria-label={showApiKey ? 'Hide API key' : 'Show API key'}
                      onClick={() => setShowApiKey((prev) => !prev)}
                      edge="end"
                    >
                      {showApiKey ? <VisibilityOffIcon fontSize="small" /> : <VisibilityIcon fontSize="small" />}
                    </IconButton>
                  </InputAdornment>
                ),
              },
            }}
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
