import { Alert, Box, Button, Chip, CircularProgress, List, ListItem, ListItemText, Stack, Typography } from '@mui/material'
import { useMemo } from 'react'
import type { WorkflowExecutionNode, WorkflowExecutionNodeStatus } from '../api/workflowExecutionsApi'
import { ApprovalDialog } from './ApprovalDialog'
import {
  useCancelWorkflowExecution,
  usePauseWorkflowExecution,
  useResumeWorkflowExecution,
  useWorkflowExecution,
} from '../hooks/useWorkflowExecution'
import { useWorkflowExecutionHub } from '../hooks/useWorkflowExecutionHub'
import { useWorkflowVersions } from '../hooks/useWorkflowVersions'

interface ExecutionMonitorProps {
  executionId: string
  workflowId: string
}

const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled', 'TimedOut'])

const NODE_STATUS_COLOR: Record<WorkflowExecutionNodeStatus, 'default' | 'info' | 'success' | 'error' | 'warning'> = {
  Pending: 'default',
  Running: 'info',
  Completed: 'success',
  Failed: 'error',
  Skipped: 'default',
  Cancelled: 'warning',
  WaitingForApproval: 'warning',
}

/**
 * Live per-node status, running usage/cost, and pause/resume/cancel controls for a workflow
 * execution (spec.md User Story 6, FR-048/FR-049). `useWorkflowExecution` polls every 2s while
 * non-terminal; `useWorkflowExecutionHub` pushes over `WorkflowExecutionHub` to shorten that
 * latency, invalidating the same query cache — the REST poll remains the source of truth and
 * reconciliation fallback if a live push is missed (constitution §2.VIII: `isLive: false` is
 * rendered as a visible "reconnecting" indicator, never a silent degradation).
 */
export function ExecutionMonitor({ executionId, workflowId }: ExecutionMonitorProps) {
  const { data: execution, isLoading, error } = useWorkflowExecution(executionId)
  const { isLive } = useWorkflowExecutionHub(executionId)
  const { data: versions } = useWorkflowVersions(workflowId)
  const pauseExecution = usePauseWorkflowExecution(executionId)
  const resumeExecution = useResumeWorkflowExecution(executionId)
  const cancelExecution = useCancelWorkflowExecution(executionId)

  const nodeLabelsById = useMemo(() => {
    const version = versions?.find((v) => v.id === execution?.workflowVersionId)
    return new Map((version?.nodes ?? []).map((n) => [n.id, n.name || n.nodeKey]))
  }, [versions, execution?.workflowVersionId])

  if (isLoading) {
    return <CircularProgress size={24} />
  }

  if (error || !execution) {
    return <Alert severity="error">This execution could not be found.</Alert>
  }

  const isRunning = !TERMINAL_STATUSES.has(execution.status)
  const pendingApproval = execution.status === 'WaitingForApproval' ? execution.approvals.find((a) => a.decision === 'Pending') : undefined

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2 }}>
        <Chip
          label={execution.status}
          color={execution.status === 'Completed' ? 'success' : execution.status === 'Failed' ? 'error' : execution.status === 'Cancelled' ? 'warning' : 'default'}
          size="small"
        />
        {/* Purely decorative — the adjacent status Chip already announces "Running" as text
            (found via ExecutionMonitor.a11y.test.tsx: an unlabeled role="progressbar" needs an
            accessible name; hiding it avoids a redundant announcement instead). */}
        {isRunning && <CircularProgress size={16} aria-hidden="true" />}
        <Chip label={isLive ? 'Live' : 'Reconnecting…'} size="small" variant="outlined" color={isLive ? 'success' : 'default'} data-testid="hub-connection-status" />

        {execution.status === 'Running' && (
          <Button size="small" onClick={() => pauseExecution.mutate()} disabled={pauseExecution.isPending}>
            Pause
          </Button>
        )}
        {execution.status === 'Paused' && (
          <Button size="small" onClick={() => resumeExecution.mutate()} disabled={resumeExecution.isPending}>
            Resume
          </Button>
        )}
        {isRunning && (
          <Button size="small" color="error" onClick={() => cancelExecution.mutate()} disabled={cancelExecution.isPending}>
            Cancel
          </Button>
        )}
      </Stack>

      {(execution.inputTokenCount !== null || execution.outputTokenCount !== null || execution.estimatedCost !== null) && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {execution.inputTokenCount ?? 0} input / {execution.outputTokenCount ?? 0} output tokens
          {execution.estimatedCost !== null && ` · $${execution.estimatedCost.toFixed(4)}`}
        </Typography>
      )}

      {execution.terminationReason && (
        <Alert severity={execution.status === 'Failed' ? 'error' : 'warning'} sx={{ mb: 2 }}>
          {execution.terminationReason}
        </Alert>
      )}

      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        Nodes
      </Typography>
      {execution.nodes.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          No nodes yet.
        </Typography>
      ) : (
        <List dense data-testid="execution-node-list">
          {execution.nodes.map((node: WorkflowExecutionNode) => (
            <ListItem key={node.id} data-testid="execution-node-row" divider>
              <ListItemText
                primary={nodeLabelsById.get(node.workflowNodeId) ?? node.workflowNodeId}
                secondary={node.startedAtUtc ? new Date(node.startedAtUtc).toLocaleTimeString() : 'not started'}
              />
              <Chip label={node.status} size="small" color={NODE_STATUS_COLOR[node.status]} />
            </ListItem>
          ))}
        </List>
      )}

      {pendingApproval && <ApprovalDialog executionId={execution.id} approval={pendingApproval} />}
    </Box>
  )
}
