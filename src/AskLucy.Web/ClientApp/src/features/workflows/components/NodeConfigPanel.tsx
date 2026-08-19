import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Chip,
  Divider,
  MenuItem,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import { useState } from 'react'
import type { WorkflowNodeApprovalPolicy, WorkflowValidationIssue, WorkflowVariableKind, WorkflowVariableType } from '../api/workflowsApi'
import { getNodeCatalogEntry } from '../nodeCatalog'
import { useWorkflowCanvasStore, type WorkflowCanvasNodeData } from '../store/workflowCanvasStore'
import { EmptyState } from '../../../components/EmptyState'

const APPROVAL_POLICIES: WorkflowNodeApprovalPolicy[] = ['NeverRequire', 'AlwaysRequire', 'AboveRiskLevel', 'ForThisNodeType']
const VARIABLE_TYPES: WorkflowVariableType[] = ['String', 'Number', 'Boolean', 'Date', 'Json', 'Text', 'File', 'Document', 'Collection']

interface NodeConfigPanelProps {
  validationIssues: WorkflowValidationIssue[]
}

/**
 * spec.md User Story 2 "Node Configuration UI" — shows the selected node's editable fields plus
 * any validation errors scoped to it. When no node is selected, shows workflow-level settings
 * (declared variables, inputs/outputs schema, error policy) instead of a bare placeholder — the
 * task list has no separate "Workflow Settings" component, and these fields need *some* editable
 * surface in the Designer.
 */
export function NodeConfigPanel({ validationIssues }: NodeConfigPanelProps) {
  const selectedNodeId = useWorkflowCanvasStore((s) => s.selectedNodeId)
  const node = useWorkflowCanvasStore((s) => s.nodes.find((n) => n.id === s.selectedNodeId))
  const updateNodeData = useWorkflowCanvasStore((s) => s.updateNodeData)
  const removeNodes = useWorkflowCanvasStore((s) => s.removeNodes)

  if (!selectedNodeId || !node) {
    return <WorkflowSettingsPanel validationIssues={validationIssues.filter((i) => i.nodeKey === null)} />
  }

  const nodeIssues = validationIssues.filter((i) => i.nodeKey === node.data.nodeKey)
  const entry = getNodeCatalogEntry(node.data.nodeType)

  const patch = (fields: Partial<WorkflowCanvasNodeData>) => updateNodeData(node.id, fields)

  return (
    <Box key={node.id} sx={{ height: '100%', overflowY: 'auto', p: 2 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="subtitle1" component="div">{entry.label}</Typography>
        <Button size="small" color="error" startIcon={<DeleteOutlineIcon />} onClick={() => removeNodes([node.id])}>
          Delete
        </Button>
      </Stack>
      <Typography variant="caption" color="text.secondary">
        {entry.description}
      </Typography>

      {nodeIssues.length > 0 && (
        <Alert severity="error" sx={{ mt: 1.5 }}>
          <Stack spacing={0.5}>
            {nodeIssues.map((issue, index) => (
              <Typography key={index} variant="body2">
                {issue.message}
              </Typography>
            ))}
          </Stack>
        </Alert>
      )}

      <Stack spacing={2} sx={{ mt: 2 }}>
        <TextField label="Name" fullWidth size="small" value={node.data.name} onChange={(e) => patch({ name: e.target.value })} />
        <TextField
          label="Description"
          fullWidth
          size="small"
          multiline
          minRows={2}
          value={node.data.description ?? ''}
          onChange={(e) => patch({ description: e.target.value || null })}
        />
        <DebouncedJsonField
          label="Configuration"
          value={node.data.configurationJson}
          onCommit={(value) => patch({ configurationJson: value })}
          helperText="Field names for this node type: see the palette entry’s description. Values may reference prior steps as &#123;&#123;steps.nodeKey.field&#125;&#125; or workflow inputs as &#123;&#123;workflow.field&#125;&#125;."
        />
      </Stack>

      <Accordion elevation={0} disableGutters sx={{ mt: 2, '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="subtitle2" component="div">Inputs / Outputs</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <Stack spacing={2}>
            <DebouncedJsonField label="Input Schema" value={node.data.inputSchemaJson} onCommit={(value) => patch({ inputSchemaJson: value })} />
            <DebouncedJsonField label="Output Schema" value={node.data.outputSchemaJson} onCommit={(value) => patch({ outputSchemaJson: value })} />
          </Stack>
        </AccordionDetails>
      </Accordion>

      <Accordion elevation={0} disableGutters sx={{ '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="subtitle2" component="div">Permissions &amp; Timeout</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <Stack spacing={2}>
            <DebouncedJsonField
              label="Required Permissions"
              value={node.data.requiredPermissionsJson}
              onCommit={(value) => patch({ requiredPermissionsJson: value })}
            />
            <TextField
              label="Timeout (seconds)"
              type="number"
              size="small"
              fullWidth
              value={node.data.timeoutSeconds ?? ''}
              onChange={(e) => patch({ timeoutSeconds: e.target.value === '' ? null : Number(e.target.value) })}
            />
          </Stack>
        </AccordionDetails>
      </Accordion>

      <Accordion elevation={0} disableGutters sx={{ '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="subtitle2" component="div">Approval Policy</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <TextField select label="Approval Policy" size="small" fullWidth value={node.data.approvalPolicy} onChange={(e) => patch({ approvalPolicy: e.target.value as WorkflowNodeApprovalPolicy })}>
            {APPROVAL_POLICIES.map((policy) => (
              <MenuItem key={policy} value={policy}>
                {policy}
              </MenuItem>
            ))}
          </TextField>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
            This can only make approval stricter than the platform’s own risk-based baseline — never bypass it (FR-035/FR-036).
          </Typography>
        </AccordionDetails>
      </Accordion>

      <Accordion elevation={0} disableGutters sx={{ '&:before': { display: 'none' } }}>
        <AccordionSummary expandIcon={<ExpandMoreIcon />}>
          <Typography variant="subtitle2" component="div">Advanced Settings</Typography>
        </AccordionSummary>
        <AccordionDetails>
          <Stack spacing={2}>
            <DebouncedJsonField
              label="Retry Policy"
              value={node.data.retryPolicyJson ?? ''}
              onCommit={(value) => patch({ retryPolicyJson: value || null })}
              placeholder='{"maxAttempts":3,"initialDelaySeconds":1,"backoffStrategy":"Exponential"}'
            />
            <TextField
              label="Idempotency Key Expression"
              size="small"
              fullWidth
              value={node.data.idempotencyKeyExpression ?? ''}
              onChange={(e) => patch({ idempotencyKeyExpression: e.target.value || null })}
              placeholder="{{workflow.orderId}}"
            />
            <TextField
              label="Compensating Node Key"
              size="small"
              fullWidth
              value={node.data.compensatingNodeKey ?? ''}
              onChange={(e) => patch({ compensatingNodeKey: e.target.value || null })}
            />
          </Stack>
        </AccordionDetails>
      </Accordion>
    </Box>
  )
}

function WorkflowSettingsPanel({ validationIssues }: { validationIssues: WorkflowValidationIssue[] }) {
  const variables = useWorkflowCanvasStore((s) => s.variables)
  const addVariable = useWorkflowCanvasStore((s) => s.addVariable)
  const removeVariable = useWorkflowCanvasStore((s) => s.removeVariable)
  const inputsSchemaJson = useWorkflowCanvasStore((s) => s.inputsSchemaJson)
  const outputsSchemaJson = useWorkflowCanvasStore((s) => s.outputsSchemaJson)
  const errorPolicyJson = useWorkflowCanvasStore((s) => s.errorPolicyJson)
  const setInputsSchemaJson = useWorkflowCanvasStore((s) => s.setInputsSchemaJson)
  const setOutputsSchemaJson = useWorkflowCanvasStore((s) => s.setOutputsSchemaJson)
  const setErrorPolicyJson = useWorkflowCanvasStore((s) => s.setErrorPolicyJson)

  const [newVariableName, setNewVariableName] = useState('')
  const [newVariableType, setNewVariableType] = useState<WorkflowVariableType>('String')

  const submitNewVariable = () => {
    const name = newVariableName.trim()
    if (!name) return
    addVariable(name, newVariableType, 'WorkflowVariable' as WorkflowVariableKind)
    setNewVariableName('')
  }

  return (
    <Box sx={{ height: '100%', overflowY: 'auto', p: 2 }}>
      <Typography variant="subtitle1" component="div" sx={{ mb: 1 }}>
        Workflow Settings
      </Typography>

      {validationIssues.length > 0 && (
        <Alert severity="error" sx={{ mb: 2 }}>
          <Stack spacing={0.5}>
            {validationIssues.map((issue, index) => (
              <Typography key={index} variant="body2">
                {issue.message}
              </Typography>
            ))}
          </Stack>
        </Alert>
      )}

      {!validationIssues.length && <EmptyState title="Select a node" description="Click a node on the canvas to configure it, or manage workflow-level settings here." />}

      <Typography variant="subtitle2" component="div" sx={{ mt: 2, mb: 1 }}>
        Variables
      </Typography>
      <Stack spacing={1} sx={{ mb: 1 }}>
        {variables.map((variable) => (
          <Stack key={variable.name} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Chip label={`${variable.name}: ${variable.valueType}`} size="small" onDelete={() => removeVariable(variable.name)} />
          </Stack>
        ))}
      </Stack>
      <Stack direction="row" spacing={1}>
        <TextField
          size="small"
          label="New variable"
          value={newVariableName}
          onChange={(e) => setNewVariableName(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && submitNewVariable()}
        />
        <TextField select size="small" label="Type" sx={{ minWidth: 120 }} value={newVariableType} onChange={(e) => setNewVariableType(e.target.value as WorkflowVariableType)}>
          {VARIABLE_TYPES.map((type) => (
            <MenuItem key={type} value={type}>
              {type}
            </MenuItem>
          ))}
        </TextField>
        <Button onClick={submitNewVariable}>Add</Button>
      </Stack>

      <Divider sx={{ my: 2 }} />

      <Stack spacing={2}>
        <DebouncedJsonField label="Declared Inputs Schema" value={inputsSchemaJson} onCommit={setInputsSchemaJson} />
        <DebouncedJsonField label="Declared Outputs Schema" value={outputsSchemaJson} onCommit={setOutputsSchemaJson} />
        <DebouncedJsonField label="Error Policy" value={errorPolicyJson} onCommit={setErrorPolicyJson} />
      </Stack>
    </Box>
  )
}

/** A raw-JSON textarea that only pushes an undo-history entry / marks the draft dirty when the field blurs — not on every keystroke. */
function DebouncedJsonField({
  label,
  value,
  onCommit,
  helperText,
  placeholder,
}: {
  label: string
  value: string
  onCommit: (value: string) => void
  helperText?: string
  placeholder?: string
}) {
  // No effect/ref syncing `draft` back to `value` — the caller remounts this field (via a `key`
  // scoped to the selected node) whenever `value` can change out from under it, so the
  // `useState(value)` initializer alone is enough to pick up the new value on every remount
  // (React's documented "reset state with a key" pattern, the one this stricter lint config
  // actually wants over the classic "compare against a ref during render" recipe).
  const [draft, setDraft] = useState(value)
  const [isInvalid, setIsInvalid] = useState(false)

  const commit = () => {
    if (draft === value) return
    if (draft.trim().length > 0) {
      try {
        JSON.parse(draft)
      } catch {
        setIsInvalid(true)
        return
      }
    }
    setIsInvalid(false)
    onCommit(draft)
  }

  return (
    <TextField
      label={label}
      fullWidth
      size="small"
      multiline
      minRows={2}
      maxRows={10}
      value={draft}
      placeholder={placeholder}
      onChange={(e) => {
        setDraft(e.target.value)
        setIsInvalid(false)
      }}
      onBlur={commit}
      error={isInvalid}
      helperText={isInvalid ? 'Not valid JSON.' : helperText}
      slotProps={{ input: { sx: { fontFamily: 'monospace', fontSize: '0.8125rem' } } }}
    />
  )
}
