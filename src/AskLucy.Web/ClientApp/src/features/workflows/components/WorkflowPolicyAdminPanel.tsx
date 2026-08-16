import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as workflowPoliciesApi from '../api/workflowPoliciesApi'
import type { WorkflowPolicy } from '../api/workflowPoliciesApi'
import type { WorkflowNodeType } from '../api/workflowsApi'

const WORKFLOW_POLICIES_QUERY_KEY = ['admin', 'workflow-policies']

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

/**
 * Administrator-managed auto-approval rule CRUD for the workflow engine's platform-mandatory
 * approval baseline (spec.md "Approval Policies") — lets a Human Approval node or a High/Critical-risk
 * capability node proceed without an interactive approval prompt when it matches an enabled policy.
 * Only reachable by Administrator/Super User (the API itself enforces this regardless of what
 * renders this component). Unlike the Agent Runtime's single `toolName` targeting, a policy here
 * targets a node type and/or an underlying tool name (mirrors `WorkflowPolicy.Create`'s domain rule
 * that at least one of the two must be set).
 */
export function WorkflowPolicyAdminPanel() {
  const queryClient = useQueryClient()
  const { data: policies, isLoading } = useQuery({ queryKey: WORKFLOW_POLICIES_QUERY_KEY, queryFn: workflowPoliciesApi.listWorkflowPolicies })

  const [name, setName] = useState('')
  const [workflowNodeType, setWorkflowNodeType] = useState<WorkflowNodeType | ''>('')
  const [underlyingToolName, setUnderlyingToolName] = useState('')
  const [description, setDescription] = useState('')
  const [conditionsJson, setConditionsJson] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: WORKFLOW_POLICIES_QUERY_KEY })

  const createPolicy = useMutation({
    mutationFn: () =>
      workflowPoliciesApi.createWorkflowPolicy({
        name,
        description: description || null,
        workflowNodeType: workflowNodeType || null,
        underlyingToolName: underlyingToolName || null,
        conditionsJson: conditionsJson || null,
      }),
    onSuccess: () => {
      setName('')
      setWorkflowNodeType('')
      setUnderlyingToolName('')
      setDescription('')
      setConditionsJson('')
      setErrorMessage(null)
      invalidate()
    },
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not create the policy. Please try again.'),
  })

  const toggleEnabled = useMutation({
    mutationFn: (policy: WorkflowPolicy) =>
      workflowPoliciesApi.updateWorkflowPolicy(policy.id, {
        name: policy.name,
        description: policy.description,
        conditionsJson: policy.conditionsJson,
        isEnabled: !policy.isEnabled,
      }),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not update the policy. Please try again.'),
  })

  const deletePolicy = useMutation({
    mutationFn: (id: string) => workflowPoliciesApi.deleteWorkflowPolicy(id),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not delete the policy. Please try again.'),
  })

  const canCreate = name.trim() && (workflowNodeType || underlyingToolName.trim())

  return (
    <Box>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Workflow Auto-Approval Policies
      </Typography>

      <Paper sx={{ p: 3, mb: 3 }}>
        <Typography variant="subtitle1" sx={{ mb: 2 }}>
          New Policy
        </Typography>
        <Stack spacing={2}>
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
          <Box>
            <Button variant="contained" disabled={!canCreate || createPolicy.isPending} onClick={() => createPolicy.mutate()}>
              Create Policy
            </Button>
          </Box>
        </Stack>
        {errorMessage && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {errorMessage}
          </Alert>
        )}
      </Paper>

      <TableContainer component={Paper}>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Node Type</TableCell>
              <TableCell>Underlying Tool</TableCell>
              <TableCell>Conditions</TableCell>
              <TableCell>Enabled</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={6}>Loading…</TableCell>
              </TableRow>
            )}
            {!isLoading && (policies ?? []).length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>No policies configured yet.</TableCell>
              </TableRow>
            )}
            {(policies ?? []).map((policy) => (
              <TableRow key={policy.id}>
                <TableCell>{policy.name}</TableCell>
                <TableCell>{policy.workflowNodeType && <Chip label={policy.workflowNodeType} size="small" />}</TableCell>
                <TableCell>{policy.underlyingToolName && <Chip label={policy.underlyingToolName} size="small" />}</TableCell>
                <TableCell>{policy.conditionsJson ?? 'Always'}</TableCell>
                <TableCell>
                  <Switch checked={policy.isEnabled} onChange={() => toggleEnabled.mutate(policy)} disabled={toggleEnabled.isPending} />
                </TableCell>
                <TableCell align="right">
                  <IconButton aria-label={`Delete ${policy.name}`} onClick={() => deletePolicy.mutate(policy.id)} disabled={deletePolicy.isPending}>
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  )
}
