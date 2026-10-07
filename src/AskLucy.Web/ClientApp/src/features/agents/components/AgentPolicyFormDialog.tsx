import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
import { useT } from '../../../i18n/useT'
import type { SaveAgentPolicyInput } from '../api/agentPoliciesApi'

interface AgentPolicyFormDialogProps {
  open: boolean
  isSaving: boolean
  errorMessage: string | null
  onClose: () => void
  onSubmit: (input: SaveAgentPolicyInput) => void
}

/** specs/062 US4 — "New Policy" moved from an inline Paper section into a modal, mirroring McpServerForm.tsx. */
export function AgentPolicyFormDialog({ open, isSaving, errorMessage, onClose, onSubmit }: AgentPolicyFormDialogProps) {
  const t = useT('admin.agentPolicies')
  const [name, setName] = useState('')
  const [toolName, setToolName] = useState('')
  const [description, setDescription] = useState('')
  const [conditionsJson, setConditionsJson] = useState('')

  const canCreate = name.trim() !== '' && toolName.trim() !== ''

  const handleSubmit = () => {
    onSubmit({
      name,
      description: description || null,
      toolName,
      conditionsJson: conditionsJson || null,
    })
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{t('dialog.title')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label={t('dialog.name')} required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField
            label={t('dialog.toolName')}
            required
            value={toolName}
            onChange={(e) => setToolName(e.target.value)}
            helperText={t('dialog.toolNameHelp')}
          />
          <TextField label={t('dialog.description')} multiline minRows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          <TextField
            label={t('dialog.conditions')}
            multiline
            minRows={2}
            value={conditionsJson}
            onChange={(e) => setConditionsJson(e.target.value)}
            helperText={t('dialog.conditionsHelp')}
          />
          {errorMessage && <Alert severity="error">{errorMessage}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('dialog.cancel')}</Button>
        <Button variant="contained" disabled={!canCreate || isSaving} onClick={handleSubmit}>
          {t('dialog.create')}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
