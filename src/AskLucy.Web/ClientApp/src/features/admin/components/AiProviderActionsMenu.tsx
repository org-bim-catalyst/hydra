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
import { useT } from '../../../i18n/useT'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

const ADMIN_AI_PROVIDERS_QUERY_KEY = ['admin', 'ai-providers']

interface AiProviderActionsMenuProps {
  provider: AdminAiProvider
}

type PendingAction = 'enable' | 'disable' | 'clearCredential' | null

type Feedback = { severity: 'success' | 'error'; message: string } | null

/**
 * specs/007-admin-ai-provider-ui — enable/disable/set-credential/clear-credential actions
 * for one AI provider row, each confirm-gated (FR-010). Mirrors UserActionMenu.tsx's
 * menu + confirm-dialog composition.
 */
export function AiProviderActionsMenu({ provider }: AiProviderActionsMenuProps) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [credentialDialogOpen, setCredentialDialogOpen] = useState(false)
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [showApiKey, setShowApiKey] = useState(false)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ADMIN_AI_PROVIDERS_QUERY_KEY })

  const onError = (err: unknown) => {
    const message = err instanceof ApiError ? err.detail ?? err.message : t('shared.genericError')
    setFeedback({ severity: 'error', message })
  }

  const updateMutation = useMutation({
    mutationFn: (isEnabled: boolean) => adminAiProvidersApi.updateProvider(provider.id, { isEnabled }),
    onSuccess: (_, isEnabled) => {
      invalidate()
      setFeedback({ severity: 'success', message: isEnabled ? t('actions.providerEnabled') : t('actions.providerDisabled') })
    },
    onError,
  })

  const setCredentialMutation = useMutation({
    mutationFn: (apiKey: string) => adminAiProvidersApi.setCredential(provider.id, apiKey),
    onSuccess: () => {
      invalidate()
      setFeedback({ severity: 'success', message: t('actions.credentialSaved') })
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
          ? { severity: 'success', message: t('actions.isHealthy', { name: provider.displayName }) }
          : {
              severity: 'error',
              // The server's own classified prose — already administrator-facing and free of
              // any vendor body or credential (FR-013).
              message: result.healthFailureReason ?? t('actions.isUnhealthy', { name: provider.displayName }),
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
      setFeedback({ severity: 'success', message: t('actions.credentialCleared') })
    },
    onError,
  })

  const handleEnableDisableClick = () => {
    if (!provider.isEnabled && !provider.hasCredential) {
      // FR-003: already known client-side from the fetched row — no API call, no
      // confirmation dialog, just the explanation immediately.
      setFeedback({ severity: 'error', message: t('actions.needsCredential') })
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
      setFeedback({ severity: 'error', message: t('actions.apiKeyRequired') })
      return
    }
    setCredentialMutation.mutate(apiKeyInput);
    closeCredentialDialog()
  }

  const credentialLabel = provider.hasCredential ? t('actions.replaceCredential') : t('actions.setCredential')
  const credentialAria = provider.hasCredential
    ? t('actions.replaceCredentialFor', { name: provider.displayName })
    : t('actions.setCredentialFor', { name: provider.displayName })
  const enableDisableLabel = provider.isEnabled ? t('actions.disable') : t('actions.enable')
  const enableDisableAria = provider.isEnabled
    ? t('actions.disableProvider', { name: provider.displayName })
    : t('actions.enableProvider', { name: provider.displayName })
  const checkNowLabel = checkHealthMutation.isPending ? t('actions.checking') : t('actions.checkNow')
  const confirmCopy = {
    enable: { title: t('actions.confirmEnableTitle'), body: t('actions.confirmEnableBody') },
    disable: { title: t('actions.confirmDisableTitle'), body: t('actions.confirmDisableBody') },
    clearCredential: { title: t('actions.confirmClearTitle'), body: t('actions.confirmClearBody') },
  }

  return (
    <>
      <Box sx={{ display: 'flex', gap: 0.5, justifyContent: 'flex-end' }}>
        <Tooltip title={credentialLabel}>
          <IconButton size="small" aria-label={credentialAria} onClick={openCredentialDialog}>
            <KeyIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title={enableDisableLabel}>
          <IconButton
            size="small"
            aria-label={enableDisableAria}
            onClick={handleEnableDisableClick}
          >
            <PowerSettingsNewIcon fontSize="small" />
          </IconButton>
        </Tooltip>
        <Tooltip title={checkNowLabel}>
          <span>
            <IconButton
              size="small"
              aria-label={t('actions.checkNowFor', { name: provider.displayName })}
              disabled={!provider.hasCredential || checkHealthMutation.isPending}
              onClick={() => checkHealthMutation.mutate()}
            >
              <HealthAndSafetyIcon fontSize="small" />
            </IconButton>
          </span>
        </Tooltip>
        <Tooltip title={t('actions.clearCredential')}>
          <span>
            <IconButton
              size="small"
              aria-label={t('actions.clearCredentialFor', { name: provider.displayName })}
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
            <DialogTitle>{confirmCopy[pendingAction].title}</DialogTitle>
            <DialogContent>
              <DialogContentText>{confirmCopy[pendingAction].body}</DialogContentText>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setPendingAction(null)}>{t('shared.cancel')}</Button>
              <Button onClick={handleConfirm} color={pendingAction === 'enable' ? 'primary' : 'error'} variant="contained" autoFocus>
                {t('shared.confirm')}
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>

      <Dialog open={credentialDialogOpen} onClose={closeCredentialDialog} maxWidth="sm" fullWidth>
        <DialogTitle>
          {credentialAria}
        </DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            {t('actions.credentialNeverShown')}
          </DialogContentText>
          <TextField
            label={t('actions.apiKey')}
            type={showApiKey ? 'text' : 'password'}
            placeholder={t('actions.apiKeyPlaceholder')}
            fullWidth
            autoFocus
            value={apiKeyInput}
            onChange={(e) => setApiKeyInput(e.target.value)}
            slotProps={{
              input: {
                endAdornment: apiKeyInput.length > 0 && (
                  <InputAdornment position="end">
                    <IconButton
                      aria-label={showApiKey ? t('actions.hideApiKey') : t('actions.showApiKey')}
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
          <Button onClick={closeCredentialDialog}>{t('shared.cancel')}</Button>
          <Button onClick={handleCredentialConfirm} variant="contained">
            {t('shared.confirm')}
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
