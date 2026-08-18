import { Box, Chip, Paper, Stack, Typography } from '@mui/material'
import { useCompareExecutions } from '../hooks/usePromptExecution'

interface ExecutionComparisonProps {
  executionIds: string[]
}

/**
 * Side-by-side execution comparison (spec.md FR-045, SC-009) — provider/model/version/
 * generation-parameters are labeled per column so the difference between executions is clear
 * without cross-referencing separate screens.
 */
export function ExecutionComparison({ executionIds }: ExecutionComparisonProps) {
  const { data: executions, isLoading } = useCompareExecutions(executionIds)

  if (executionIds.length < 2) {
    return (
      <Typography variant="body2" color="text.secondary">
        Select two or more executions from the history to compare them side by side.
      </Typography>
    )
  }

  if (isLoading || !executions) {
    return null
  }

  return (
    <Box data-testid="execution-comparison" sx={{ display: 'flex', gap: 2, overflowX: 'auto' }}>
      {executions.map((execution) => (
        <Paper key={execution.id} variant="outlined" sx={{ p: 2, minWidth: 320, flex: '0 0 320px' }}>
          <Stack spacing={1}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Chip size="small" label={execution.providerKey} />
              <Chip size="small" label={execution.modelKey} />
              <Chip size="small" label={`v${execution.versionNumber}`} variant="outlined" />
            </Stack>
            <Typography variant="caption" color="text.secondary">
              temperature: {execution.temperature ?? '—'} · max tokens: {execution.maxOutputTokens ?? '—'}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {execution.latencyMs !== null && `${execution.latencyMs}ms`}
              {execution.estimatedCostUsd !== null && ` · $${execution.estimatedCostUsd.toFixed(4)}`}
              {execution.inputTokenCount !== null && ` · ${execution.inputTokenCount}→${execution.outputTokenCount} tokens`}
            </Typography>
            <Typography
              variant="body2"
              sx={{ whiteSpace: 'pre-wrap', maxHeight: 300, overflowY: 'auto', mt: 1 }}
            >
              {execution.outcome === 'Success' ? execution.outputText : execution.errorDetail}
            </Typography>
          </Stack>
        </Paper>
      ))}
    </Box>
  )
}
