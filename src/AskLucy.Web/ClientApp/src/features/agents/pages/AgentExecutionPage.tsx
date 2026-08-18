import { Alert, Box, Chip, Divider, Paper, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ExecutionTimeline } from '../components/ExecutionTimeline'
import * as agentExecutionsApi from '../api/agentExecutionsApi'
import { useAgentExecution } from '../hooks/useAgentExecution'

/**
 * Full inspectable history for one past execution (spec.md User Story 5) — steps, tool calls,
 * approvals, usage/cost, and citations assembled into a single detail view. Read-only; unlike
 * `ExecutionConsole` this never starts or controls an execution, it only displays one that's
 * already run (or is still running, in which case it happens to also update live via the same
 * polled query).
 */
export function AgentExecutionPage() {
  const { executionId } = useParams<{ agentId: string; executionId: string }>()
  const { data: execution, isLoading, error } = useAgentExecution(executionId ?? null)
  const { data: toolCalls } = useQuery({
    queryKey: ['agent-executions', executionId, 'tool-calls'],
    queryFn: () => agentExecutionsApi.getAgentToolCalls(executionId!),
    enabled: executionId !== undefined,
  })
  const { data: usage } = useQuery({
    queryKey: ['agent-executions', executionId, 'usage'],
    queryFn: () => agentExecutionsApi.getAgentExecutionUsage(executionId!),
    enabled: executionId !== undefined,
  })

  if (isLoading) {
    return (
      <AppShell title="Loading…">
        <div />
      </AppShell>
    )
  }

  if (error || !execution) {
    return (
      <AppShell title="Execution not found">
        <Alert severity="error">This execution could not be found.</Alert>
      </AppShell>
    )
  }

  const citations = execution.finalOutputJson ? (JSON.parse(execution.finalOutputJson).citations ?? []) : []

  return (
    <AppShell title="Execution history" subtitle={execution.objective}>
      <Stack spacing={3} sx={{ maxWidth: 900, mx: 'auto' }}>
        <Paper sx={{ p: 3 }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 2 }}>
            <Chip label={execution.status} color={execution.status === 'Completed' ? 'success' : execution.status === 'Failed' ? 'error' : 'default'} size="small" />
            {execution.isTestExecution && <Chip label="Test execution" size="small" variant="outlined" />}
          </Stack>

          <Typography variant="body2" color="text.secondary">
            Started {execution.startedAtUtc ? new Date(execution.startedAtUtc).toLocaleString() : '—'}
            {execution.completedAtUtc && ` · Completed ${new Date(execution.completedAtUtc).toLocaleString()}`}
          </Typography>

          {usage && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {usage.inputTokenCount ?? 0} input / {usage.outputTokenCount ?? 0} output tokens · {usage.stepCount} steps ·{' '}
              {usage.toolCallCount} tool calls
              {usage.estimatedCost !== null && ` · $${usage.estimatedCost.toFixed(4)} ${usage.costCurrency}`}
            </Typography>
          )}

          {execution.finalOutputText && (
            <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap', mt: 2 }}>
              {execution.finalOutputText}
            </Typography>
          )}

          {execution.terminationReason && (
            <Alert severity={execution.status === 'Failed' ? 'error' : 'warning'} sx={{ mt: 2 }}>
              {execution.terminationReason}
            </Alert>
          )}

          {citations.length > 0 && (
            <Box sx={{ mt: 2 }}>
              <Typography variant="subtitle2">Sources</Typography>
              {citations.map((citation: { documentTitle?: string; knowledgeBaseName?: string }, index: number) => (
                <Typography key={index} variant="body2" color="text.secondary">
                  {citation.documentTitle} ({citation.knowledgeBaseName})
                </Typography>
              ))}
            </Box>
          )}
        </Paper>

        <Paper sx={{ p: 3 }}>
          <Typography variant="subtitle1" sx={{ mb: 1 }}>
            Steps
          </Typography>
          <ExecutionTimeline steps={execution.steps} />
        </Paper>

        {toolCalls && toolCalls.length > 0 && (
          <Paper sx={{ p: 3 }}>
            <Typography variant="subtitle1" sx={{ mb: 1 }}>
              Tool Calls
            </Typography>
            <Stack divider={<Divider />} spacing={1.5}>
              {toolCalls.map((toolCall) => (
                <Box key={toolCall.id} data-testid="execution-tool-call-row">
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <Chip label={toolCall.toolName} size="small" />
                    <Chip label={toolCall.riskLevel} size="small" variant="outlined" />
                    {toolCall.failureReason ? (
                      <Chip label="Failed" size="small" color="error" />
                    ) : (
                      <Chip label="Completed" size="small" color="success" />
                    )}
                  </Stack>
                  {toolCall.failureReason && (
                    <Typography variant="body2" color="error">
                      {toolCall.failureReason}
                    </Typography>
                  )}
                </Box>
              ))}
            </Stack>
          </Paper>
        )}

        {execution.approvals.length > 0 && (
          <Paper sx={{ p: 3 }}>
            <Typography variant="subtitle1" sx={{ mb: 1 }}>
              Approvals
            </Typography>
            <Stack divider={<Divider />} spacing={1.5}>
              {execution.approvals.map((approval) => (
                <Box key={approval.id}>
                  <Typography variant="body2">{approval.intendedActionDescription}</Typography>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 0.5 }}>
                    <Chip label={approval.decision} size="small" color={approval.decision === 'Approved' ? 'success' : approval.decision === 'Rejected' ? 'error' : 'default'} />
                    {approval.wasPolicyBased && <Chip label="Auto-approved by policy" size="small" variant="outlined" />}
                  </Stack>
                </Box>
              ))}
            </Stack>
          </Paper>
        )}
      </Stack>
    </AppShell>
  )
}
