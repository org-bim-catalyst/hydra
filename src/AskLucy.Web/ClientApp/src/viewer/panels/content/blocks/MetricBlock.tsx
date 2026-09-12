import { Box, Typography } from '@mui/material'
import { ActionAffordance } from '../../actions/ActionAffordance'
import type { MetricBlock as MetricBlockData } from '../blocks'

/** contracts/content-vocabulary.md "metric" block — a single prominent figure. Renders through
 * the shared `ActionAffordance` (specs/049 US2) when it carries a valid action. */
export function MetricBlockRenderer({ block }: { block: MetricBlockData }) {
  return (
    <ActionAffordance action={block.action}>
      <Box>
        <Typography variant="caption" color="text.secondary">
          {block.label}
        </Typography>
        <Typography variant="h5" sx={{ fontWeight: 600 }}>
          {block.value}
          {block.unit && (
            <Typography component="span" variant="body2" color="text.secondary" sx={{ ml: 0.5 }}>
              {block.unit}
            </Typography>
          )}
        </Typography>
      </Box>
    </ActionAffordance>
  )
}
