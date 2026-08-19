import { Box, Card, CardContent, Skeleton, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import * as workflowsApi from '../api/workflowsApi'

function formatDuration(seconds: number | null): string {
  if (seconds === null) return '—'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`
  return `${(seconds / 3600).toFixed(1)}h`
}

function formatPercent(rate: number): string {
  return `${Math.round(rate * 100)}%`
}

function formatCurrency(amount: number): string {
  return amount.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
}

/** Workflow Monitoring dashboard aggregate, scoped to the caller's own executions (spec.md User Story 8). */
export function StatisticsDashboard() {
  const { data, isLoading } = useQuery({
    queryKey: ['workflows', 'statistics'],
    queryFn: () => workflowsApi.getWorkflowStatistics(),
  })

  const stats: { label: string; value: string }[] = [
    { label: 'Active', value: String(data?.activeCount ?? 0) },
    { label: 'Queued', value: String(data?.queuedCount ?? 0) },
    { label: 'Failed', value: String(data?.failedCount ?? 0) },
    { label: 'Completed', value: String(data?.completedCount ?? 0) },
    { label: 'Avg. duration', value: formatDuration(data?.averageDurationSeconds ?? null) },
    { label: 'Failure rate', value: formatPercent(data?.failureRate ?? 0) },
    { label: 'AI tokens used', value: String((data?.totalInputTokens ?? 0) + (data?.totalOutputTokens ?? 0)) },
    { label: 'Estimated cost', value: formatCurrency(data?.totalEstimatedCost ?? 0) },
  ]

  return (
    <Box
      data-testid="workflow-statistics-dashboard"
      sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, 1fr)', md: 'repeat(8, 1fr)' }, gap: 1.5, mb: 3 }}
    >
      {stats.map((stat) => (
        <Card key={stat.label} variant="outlined">
          <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
            <Typography variant="caption" color="text.secondary" noWrap>
              {stat.label}
            </Typography>
            {isLoading ? <Skeleton variant="text" width="60%" height={32} /> : <Typography variant="h6">{stat.value}</Typography>}
          </CardContent>
        </Card>
      ))}
    </Box>
  )
}
