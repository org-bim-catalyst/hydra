import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
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
import * as agentPoliciesApi from '../api/agentPoliciesApi'
import type { AgentPolicy } from '../api/agentPoliciesApi'

const AGENT_POLICIES_QUERY_KEY = ['admin', 'agent-policies']

/**
 * Administrator-managed auto-approval rule CRUD (spec.md FR-025/FR-026, research.md Decision 1)
 * — lets a High/Critical-risk tool call proceed without an interactive approval prompt when the
 * call matches an enabled policy. Only reachable by Administrator/Super User (the API itself
 * enforces this regardless of what renders this component).
 */
export function AgentPolicyAdminPanel() {
  const queryClient = useQueryClient()
  const { data: policies, isLoading } = useQuery({ queryKey: AGENT_POLICIES_QUERY_KEY, queryFn: agentPoliciesApi.listAgentPolicies })

  const [name, setName] = useState('')
  const [toolName, setToolName] = useState('')
  const [description, setDescription] = useState('')
  const [conditionsJson, setConditionsJson] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const invalidate = () => queryClient.invalidateQueries({ queryKey: AGENT_POLICIES_QUERY_KEY })

  const createPolicy = useMutation({
    mutationFn: () =>
      agentPoliciesApi.createAgentPolicy({
        name,
        description: description || null,
        toolName,
        conditionsJson: conditionsJson || null,
      }),
    onSuccess: () => {
      setName('')
      setToolName('')
      setDescription('')
      setConditionsJson('')
      setErrorMessage(null)
      invalidate()
    },
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not create the policy. Please try again.'),
  })

  const toggleEnabled = useMutation({
    mutationFn: (policy: AgentPolicy) =>
      agentPoliciesApi.updateAgentPolicy(policy.id, {
        name: policy.name,
        description: policy.description,
        conditionsJson: policy.conditionsJson,
        isEnabled: !policy.isEnabled,
      }),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not update the policy. Please try again.'),
  })

  const deletePolicy = useMutation({
    mutationFn: (id: string) => agentPoliciesApi.deleteAgentPolicy(id),
    onSuccess: invalidate,
    onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not delete the policy. Please try again.'),
  })

  return (
    <Box>
      <Typography variant="h6" sx={{ mb: 2 }}>
        Agent Auto-Approval Policies
      </Typography>

      <Paper sx={{ p: 3, mb: 3 }}>
        <Typography variant="subtitle1" sx={{ mb: 2 }}>
          New Policy
        </Typography>
        <Stack spacing={2}>
          <TextField label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField label="Tool Name" required value={toolName} onChange={(e) => setToolName(e.target.value)} helperText="Must exactly match the tool's registered name, e.g. FakeHighRiskTool" />
          <TextField label="Description" multiline minRows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          <TextField
            label="Conditions (JSON, optional)"
            multiline
            minRows={2}
            value={conditionsJson}
            onChange={(e) => setConditionsJson(e.target.value)}
            helperText='A flat JSON object of required parameter values, e.g. {"action":"read-only"}. Leave empty to match every call to this tool.'
          />
          <Box>
            <Button
              variant="contained"
              disabled={!name || !toolName || createPolicy.isPending}
              onClick={() => createPolicy.mutate()}
            >
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
              <TableCell>Tool</TableCell>
              <TableCell>Conditions</TableCell>
              <TableCell>Enabled</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={5}>Loading…</TableCell>
              </TableRow>
            )}
            {!isLoading && (policies ?? []).length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No policies configured yet.</TableCell>
              </TableRow>
            )}
            {(policies ?? []).map((policy) => (
              <TableRow key={policy.id}>
                <TableCell>{policy.name}</TableCell>
                <TableCell>
                  <Chip label={policy.toolName} size="small" />
                </TableCell>
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
