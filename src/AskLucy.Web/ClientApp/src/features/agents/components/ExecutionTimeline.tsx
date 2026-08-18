import { Box, Chip, List, ListItem, ListItemText, Stack, Typography } from '@mui/material'
import type { AgentExecutionStep, AgentExecutionStepStatus } from '../api/agentExecutionsApi'

interface ExecutionTimelineProps {
  steps: AgentExecutionStep[]
}

const STATUS_COLOR: Record<AgentExecutionStepStatus, 'default' | 'info' | 'success' | 'error' | 'warning'> = {
  Pending: 'default',
  Running: 'info',
  Completed: 'success',
  Failed: 'error',
  Skipped: 'default',
  Cancelled: 'warning',
  WaitingForApproval: 'warning',
}

/**
 * Live step/tool-call activity (spec.md FR-036/User Story 4) — reflects whatever
 * `useAgentExecution`'s polled data (accelerated by `useAgentExecutionHub`'s live push) currently
 * holds; renders nothing beyond a step's already-safe summary fields, never raw model reasoning
 * (FR-035).
 */
export function ExecutionTimeline({ steps }: ExecutionTimelineProps) {
  if (steps.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No steps yet.
      </Typography>
    )
  }

  return (
    <List dense data-testid="execution-timeline">
      {[...steps]
        .sort((a, b) => a.stepIndex - b.stepIndex)
        .map((step) => (
          <ListItem key={step.id} data-testid="execution-step-row" divider>
            <ListItemText
              primary={
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <Typography variant="body2">{step.description}</Typography>
                  {step.toolName && <Chip label={step.toolName} size="small" data-testid="tool-call-row" />}
                </Stack>
              }
              secondary={
                <Box component="span">
                  {step.stepType} · {step.startedAtUtc ? new Date(step.startedAtUtc).toLocaleTimeString() : 'not started'}
                </Box>
              }
            />
            <Chip label={step.status} size="small" color={STATUS_COLOR[step.status]} />
          </ListItem>
        ))}
    </List>
  )
}
