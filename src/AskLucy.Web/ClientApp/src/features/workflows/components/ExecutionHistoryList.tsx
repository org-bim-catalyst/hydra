import { Chip, List, ListItemButton, ListItemText, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router'
import * as workflowExecutionsApi from '../api/workflowExecutionsApi'
import type { WorkflowExecutionStatus } from '../api/workflowExecutionsApi'

interface ExecutionHistoryListProps {
  workflowId: string
}

const STATUS_COLOR: Record<WorkflowExecutionStatus, 'default' | 'info' | 'success' | 'error' | 'warning'> = {
  Queued: 'default',
  Running: 'info',
  Paused: 'warning',
  WaitingForApproval: 'warning',
  Completed: 'success',
  Failed: 'error',
  Cancelled: 'default',
  TimedOut: 'error',
}

/** User Story 8 — every past execution of this workflow, most recent first (spec.md FR-050/FR-051). */
export function ExecutionHistoryList({ workflowId }: ExecutionHistoryListProps) {
  const { data, isLoading } = useQuery({
    queryKey: ['workflow-executions', 'history', workflowId],
    queryFn: () => workflowExecutionsApi.listWorkflowExecutions({ workflowId, pageSize: 50 }),
  })

  if (isLoading) {
    return (
      <Typography variant="body2" color="text.secondary">
        Loading history…
      </Typography>
    )
  }

  if (!data || data.items.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No executions yet.
      </Typography>
    )
  }

  return (
    <List dense data-testid="execution-history-list">
      {data.items.map((execution) => (
        <ListItemButton
          key={execution.id}
          component={RouterLink}
          to={`/workflows/${workflowId}/executions/${execution.id}`}
          data-testid="execution-history-row"
        >
          <ListItemText primary={new Date(execution.createdAtUtc).toLocaleString()} secondary={execution.triggerType} />
          <Chip label={execution.status} size="small" color={STATUS_COLOR[execution.status]} />
        </ListItemButton>
      ))}
    </List>
  )
}
