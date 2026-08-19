import { Paper, Stack } from '@mui/material'
import { useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ExecutionMonitor } from '../components/ExecutionMonitor'

/**
 * Live monitor for one workflow execution (spec.md User Story 6) — composes
 * {@link ExecutionMonitor} (which itself shows the pending {@link ApprovalDialog} whenever the
 * execution is `WaitingForApproval`, per User Story 5).
 */
export function WorkflowExecutionPage() {
  const { workflowId, executionId } = useParams<{ workflowId: string; executionId: string }>()

  return (
    <AppShell title="Workflow execution" subtitle="Live progress, node status, and controls for this run">
      <Stack spacing={3} sx={{ maxWidth: 900, mx: 'auto' }}>
        <Paper sx={{ p: 3 }}>
          {workflowId && executionId && <ExecutionMonitor executionId={executionId} workflowId={workflowId} />}
        </Paper>
      </Stack>
    </AppShell>
  )
}
