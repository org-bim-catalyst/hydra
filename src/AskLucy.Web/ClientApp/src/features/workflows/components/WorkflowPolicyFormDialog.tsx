import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField } from '@mui/material'
import type { WorkflowNodeType } from '../api/workflowsApi'
import type { SaveWorkflowPolicyInput } from '../api/workflowPoliciesApi'

const NODE_TYPES: WorkflowNodeType[] = [
  'AiPrompt',
  'AiAgent',
  'RagSearch',
  'MemorySearch',
  'DocumentProcessing',
  'FileOperation',
  'McpTool',
  'NativeTool',
  'HumanApproval',
]

interface WorkflowPolicyFormDialogProps {
  open: boolean
  isSaving: boolean
  errorMessage: string | null
  onClose: () => void
  onSubmit: (input: SaveWorkflowPolicyInput) => void
}

/** specs/062 US4 — "New Policy" moved from an inline Paper section into a modal, mirroring McpServerForm.tsx. */
export function WorkflowPolicyFormDialog({ open, isSaving, errorMessage, onClose, onSubmit }: WorkflowPolicyFormDialogProps) {
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
      <DialogTitle>New policy</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField
            select
            label="Node Type (optional)"
            value={workflowNodeType}
            onChange={(e) => setWorkflowNodeType(e.target.value as WorkflowNodeType | '')}
            helperText="Leave unset to target by underlying tool name alone"
          >
            <MenuItem value="">
              <em>None</em>
            </MenuItem>
            {NODE_TYPES.map((nodeType) => (
              <MenuItem key={nodeType} value={nodeType}>
                {nodeType}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label="Underlying Tool Name (optional)"
            value={underlyingToolName}
            onChange={(e) => setUnderlyingToolName(e.target.value)}
            helperText="Must exactly match the underlying capability's registered tool name, e.g. KnowledgeSearchTool"
          />
          <TextField label="Description" multiline minRows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          <TextField
            label="Conditions (JSON, optional)"
            multiline
            minRows={2}
            value={conditionsJson}
            onChange={(e) => setConditionsJson(e.target.value)}
            helperText='A flat JSON object of required parameter values, e.g. {"visibility":"public"}. Leave empty to match every matching node.'
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
