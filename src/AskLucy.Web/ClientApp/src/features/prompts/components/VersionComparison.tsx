import { Box, Grid, Paper, Typography } from '@mui/material'
import { useCompareVersions } from '../hooks/usePromptVersions'

interface VersionComparisonProps {
  promptId: string
  from: number
  to: number
}

/** Side-by-side content/variable/model-setting diff between two versions (spec.md FR-032, User Story 3 AC2). */
export function VersionComparison({ promptId, from, to }: VersionComparisonProps) {
  const { data } = useCompareVersions(promptId, from, to)

  if (!data) {
    return null
  }

  if (data.differences.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No differences between v{from} and v{to}.
      </Typography>
    )
  }

  return (
    <Box data-testid="version-comparison">
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        v{from} → v{to}
      </Typography>
      {data.differences.map((diff) => (
        <Grid container spacing={2} key={diff.fieldName} sx={{ mb: 1 }}>
          <Grid size={{ xs: 12 }}>
            <Typography variant="caption" color="text.secondary">
              {diff.fieldName}
            </Typography>
          </Grid>
          <Grid size={{ xs: 6 }}>
            <Paper variant="outlined" sx={{ p: 1, bgcolor: 'error.main', color: 'error.contrastText', opacity: 0.15 }}>
              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                {diff.fromValue ?? '—'}
              </Typography>
            </Paper>
          </Grid>
          <Grid size={{ xs: 6 }}>
            <Paper variant="outlined" sx={{ p: 1, bgcolor: 'success.main', color: 'success.contrastText', opacity: 0.15 }}>
              <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
                {diff.toValue ?? '—'}
              </Typography>
            </Paper>
          </Grid>
        </Grid>
      ))}
    </Box>
  )
}
