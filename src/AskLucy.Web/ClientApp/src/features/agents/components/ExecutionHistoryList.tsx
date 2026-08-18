import { Chip, List, ListItemButton, ListItemText, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { Link as RouterLink } from 'react-router'
import * as agentExecutionsApi from '../api/agentExecutionsApi'
import type { AgentExecutionStatus } from '../api/agentExecutionsApi'

interface ExecutionHistoryListProps {
  agentId: string
}

const STATUS_COLOR: Record<AgentExecutionStatus, 'default' | 'info' | 'success' | 'error' | 'warning'> = {
  Queued: 'default',
  Running: 'info',
  Paused: 'warning',
  WaitingForApproval: 'warning',
  Completed: 'success',
  Failed: 'error',
  Cancelled: 'default',
}

/** User Story 5 — every past execution of this agent, most recent first (spec.md FR-036/FR-050). */
export function ExecutionHistoryList({ agentId }: ExecutionHistoryListProps) {
  const { data, isLoading } = useQuery({
    queryKey: ['agent-executions', 'history', agentId],
    queryFn: () => agentExecutionsApi.listAgentExecutions({ agentId, pageSize: 50 }),
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
        <ListItemButton key={execution.id} component={RouterLink} to={`/agents/${agentId}/executions/${execution.id}`} data-testid="execution-history-row">
          <ListItemText
            primary={new Date(execution.createdAtUtc).toLocaleString()}
            secondary={execution.isTestExecution ? 'Test execution' : undefined}
          />
          <Chip label={execution.status} size="small" color={STATUS_COLOR[execution.status]} />
        </ListItemButton>
      ))}
    </List>
  )
}
