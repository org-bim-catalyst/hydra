import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Stack,
  Switch,
  Typography,
} from '@mui/material'
import { useMutation } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AiCapabilitySettings } from '../api/adminAiProvidersApi'

interface CapabilitySettingsDialogProps {
  /** The capability being configured; the dialog is open while this is set. */
  capabilitySettings: AiCapabilitySettings | null
  capabilityLabel: string
  onClose: () => void
  onSaved: () => void
}

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'

/**
 * specs/077 — the settings one capability declares, opened from its gear on the AI Capabilities
 * page. Changes are held here until Save, so Cancel leaves the stored values untouched.
 */
export function CapabilitySettingsDialog({ capabilitySettings, capabilityLabel, onClose, onSaved }: CapabilitySettingsDialogProps) {
  return (
    <Dialog open={capabilitySettings !== null} onClose={onClose} maxWidth="sm" fullWidth>
      {/*
        Keyed on the capability so every opening starts from the stored values, not from edits
        left behind by a cancelled one.
      */}
      {capabilitySettings && (
        <SettingsForm
          key={capabilitySettings.capability}
          capabilitySettings={capabilitySettings}
          capabilityLabel={capabilityLabel}
          onClose={onClose}
          onSaved={onSaved}
        />
      )}
    </Dialog>
  )
}

function SettingsForm({
  capabilitySettings,
  capabilityLabel,
  onClose,
  onSaved,
}: CapabilitySettingsDialogProps & { capabilitySettings: AiCapabilitySettings }) {
  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(capabilitySettings.settings.map((setting) => [setting.key, setting.value])),
  )

  const saveMutation = useMutation({
    mutationFn: () => adminAiProvidersApi.updateCapabilitySettings(capabilitySettings.capability, values),
    onSuccess: onSaved,
  })

  const unchanged = capabilitySettings.settings.every((setting) => values[setting.key] === setting.value)

  return (
    <>
      <DialogTitle>{`${capabilityLabel} settings`}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {capabilitySettings.settings.map((setting) => (
            <div key={setting.key}>
              <FormControlLabel
                control={
                  <Switch
                    checked={values[setting.key] === 'true'}
                    disabled={saveMutation.isPending}
                    onChange={(event) =>
                      setValues((current) => ({ ...current, [setting.key]: event.target.checked ? 'true' : 'false' }))
                    }
                  />
                }
                label={setting.label}
              />
              <Typography variant="body2" color="text.secondary" sx={{ ml: 6 }}>
                {setting.description}
              </Typography>
            </div>
          ))}
        </Stack>
        {saveMutation.isError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {errorMessage(saveMutation.error)}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={unchanged || saveMutation.isPending} onClick={() => saveMutation.mutate()}>
          {saveMutation.isPending ? 'Saving…' : 'Save'}
        </Button>
      </DialogActions>
    </>
  )
}
