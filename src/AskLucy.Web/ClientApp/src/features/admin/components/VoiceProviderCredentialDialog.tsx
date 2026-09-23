import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  TextField,
} from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminVoiceApi from '../api/adminVoiceApi'
import type { AdminVoiceProvider } from '../api/adminVoiceApi'

interface VoiceProviderCredentialDialogProps {
  provider: AdminVoiceProvider | null
  onClose: () => void
  onSaved: (provider: AdminVoiceProvider) => void
}

/** specs/070 — sets or replaces a voice provider's API key. The value is never read back. */
export function VoiceProviderCredentialDialog({ provider, onClose, onSaved }: VoiceProviderCredentialDialogProps) {
  const [apiKey, setApiKey] = useState('')

  const saveMutation = useMutation({
    mutationFn: ({ id, key }: { id: string; key: string }) => adminVoiceApi.setVoiceProviderCredential(id, key),
    onSuccess: (saved) => {
      onSaved(saved)
      close()
    },
  })

  function close() {
    setApiKey('')
    saveMutation.reset()
    onClose()
  }

  return (
    <Dialog open={provider !== null} onClose={close} maxWidth="sm" fullWidth>
      <DialogTitle>
        {provider?.hasCredential ? 'Replace API key for' : 'Set API key for'} {provider?.displayName}
      </DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>The value is never shown again once saved.</DialogContentText>
        <TextField
          label="API key"
          type="password"
          autoComplete="off"
          fullWidth
          autoFocus
          value={apiKey}
          onChange={(event) => setApiKey(event.target.value)}
        />
        {saveMutation.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {saveMutation.error instanceof ApiError
              ? saveMutation.error.detail ?? saveMutation.error.message
              : 'Something went wrong. Please try again.'}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!provider || !apiKey.trim() || saveMutation.isPending}
          onClick={() => provider && saveMutation.mutate({ id: provider.id, key: apiKey.trim() })}
        >
          {saveMutation.isPending ? 'Saving…' : 'Save'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
