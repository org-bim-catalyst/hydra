import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField } from '@mui/material'
import { useT } from '../../../i18n/useT'
import type { WorkflowNodeType } from '../api/workflowsApi'
import { NODE_TYPES } from './workflowPolicyNodeTypes'
import type { SaveWorkflowPolicyInput } from '../api/workflowPoliciesApi'

interface WorkflowPolicyFormDialogProps {
  open: boolean
  isSaving: boolean
  errorMessage: string | null
  onClose: () => void
  onSubmit: (input: SaveWorkflowPolicyInput) => void
}

/** specs/062 US4 — "New Policy" moved from an inline Paper section into a modal, mirroring McpServerForm.tsx. */
export function WorkflowPolicyFormDialog({ open, isSaving, errorMessage, onClose, onSubmit }: WorkflowPolicyFormDialogProps) {
  const t = useT('admin.workflowPolicies')
  const [name, setName] = useState('')
  const [workflowNodeType, setWorkflowNodeType] = useState<WorkflowNodeType | ''>('')
  const [underlyingToolName, setUnderlyingToolName] = useState('')
  const [description, setDescription] = useState('')
  const [conditionsJson, setConditionsJson] = useState('')

  const canCreate = name.trim() !== '' && (workflowNodeType !== '' || underlyingToolName.trim() !== '')

  const handleSubmit = () => {
    onSubmit({
      name,
      description: description || null,
      workflowNodeType: workflowNodeType || null,
      underlyingToolName: underlyingToolName || null,
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
            select
            label={t('dialog.nodeType')}
            value={workflowNodeType}
            onChange={(e) => setWorkflowNodeType(e.target.value as WorkflowNodeType | '')}
            helperText={t('dialog.nodeTypeHelp')}
          >
            <MenuItem value="">
              <em>{t('dialog.none')}</em>
            </MenuItem>
            {NODE_TYPES.map((nodeType) => (
              <MenuItem key={nodeType} value={nodeType}>
                {t(`nodeTypes.${nodeType}`)}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label={t('dialog.underlyingToolName')}
            value={underlyingToolName}
            onChange={(e) => setUnderlyingToolName(e.target.value)}
            helperText={t('dialog.underlyingToolNameHelp')}
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
