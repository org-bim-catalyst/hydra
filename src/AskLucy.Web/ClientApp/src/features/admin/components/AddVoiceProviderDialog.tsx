import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControl,
  InputLabel,
  MenuItem,
  Select,
  TextField,
} from '@mui/material'
import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminVoiceApi from '../api/adminVoiceApi'
import type { AdminVoiceProvider } from '../api/adminVoiceApi'

interface AddVoiceProviderDialogProps {
  open: boolean
  onClose: () => void
  onAdded: (provider: AdminVoiceProvider) => void
}

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'

/**
 * specs/070 — adds one of the voice engines this server has installed. The list comes from the
 * server rather than a hard-coded set, so a new engine appears here once it is deployed.
 */
export function AddVoiceProviderDialog({ open, onClose, onAdded }: AddVoiceProviderDialogProps) {
  const [providerKey, setProviderKey] = useState('')
  const [apiKey, setApiKey] = useState('')

  const enginesQuery = useQuery({
    queryKey: adminVoiceApi.VOICE_QUERY_KEYS.engines,
    queryFn: adminVoiceApi.getVoiceEngines,
    enabled: open,
  })
  const available = (enginesQuery.data ?? []).filter((engine) => !engine.isAdded)
  const selected = available.find((engine) => engine.providerKey === providerKey)

  const addMutation = useMutation({
    mutationFn: () => adminVoiceApi.addVoiceProvider(providerKey, apiKey.trim() || null),
    onSuccess: (provider) => {
      onAdded(provider)
      close()
    },
  })

  function close() {
    setProviderKey('')
    setApiKey('')
    addMutation.reset()
    onClose()
  }

  return (
    <Dialog open={open} onClose={close} maxWidth="sm" fullWidth>
      <DialogTitle>Add voice provider</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          A new provider joins the end of the failover order. Make it Lucy&apos;s voice from the page once you have
          auditioned its voices.
        </DialogContentText>
        {enginesQuery.isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button color="inherit" size="small" onClick={() => void enginesQuery.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(enginesQuery.error)}
          </Alert>
        )}
        {enginesQuery.isSuccess && available.length === 0 && (
          <Alert severity="info" sx={{ mb: 2 }}>
            Every voice engine installed on this server has already been added.
          </Alert>
        )}
        <FormControl fullWidth sx={{ mt: 1 }} disabled={available.length === 0}>
          <InputLabel id="add-voice-provider-engine-label">Provider</InputLabel>
          <Select
            labelId="add-voice-provider-engine-label"
            label="Provider"
            value={selected ? providerKey : ''}
            onChange={(event) => setProviderKey(event.target.value)}
          >
            {available.map((engine) => (
              <MenuItem key={engine.providerKey} value={engine.providerKey}>
                {engine.displayName}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
        {selected?.requiresCredential && (
          <TextField
            label="API key"
            type="password"
            autoComplete="off"
            fullWidth
            sx={{ mt: 2 }}
            value={apiKey}
            onChange={(event) => setApiKey(event.target.value)}
            helperText="Stored encrypted and never shown again. You can also set it later."
          />
        )}
        {addMutation.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {errorMessage(addMutation.error)}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={close}>Cancel</Button>
        <Button
          variant="contained"
          disabled={!selected || addMutation.isPending}
          onClick={() => addMutation.mutate()}
        >
          {addMutation.isPending ? 'Adding…' : 'Add'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
