import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField } from '@mui/material'
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
      <DialogTitle>New policy</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField
            label="Tool Name"
            required
            value={toolName}
            onChange={(e) => setToolName(e.target.value)}
            helperText="Must exactly match the tool's registered name, e.g. FakeHighRiskTool"
          />
          <TextField label="Description" multiline minRows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          <TextField
            label="Conditions (JSON, optional)"
            multiline
            minRows={2}
            value={conditionsJson}
            onChange={(e) => setConditionsJson(e.target.value)}
            helperText='A flat JSON object of required parameter values, e.g. {"action":"read-only"}. Leave empty to match every call to this tool.'
          />
          {errorMessage && <Alert severity="error">{errorMessage}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!canCreate || isSaving} onClick={handleSubmit}>
          Create Policy
        </Button>
      </DialogActions>
    </Dialog>
  )
}
