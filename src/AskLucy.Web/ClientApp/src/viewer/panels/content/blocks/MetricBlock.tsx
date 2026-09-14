import { Box, Typography } from '@mui/material'
import { ActionAffordance } from '../../actions/ActionAffordance'
import { COMPACT_ACCENT, COMPACT_MONO_FONT, compactRowSx } from '../../chrome/compactStyles'
import { usePanelDensity } from '../../chrome/density'
import type { MetricBlock as MetricBlockData } from '../blocks'

/** contracts/content-vocabulary.md "metric" block — a single prominent figure. Renders through
 * the shared `ActionAffordance` (specs/049 US2) when it carries a valid action. In a compact panel
 * the figure is a highlighted row — label left, amber monospace value right — rather than a large
 * standalone number. */
export function MetricBlockRenderer({ block }: { block: MetricBlockData }) {
  const density = usePanelDensity()

  if (density === 'compact') {
    return (
      <ActionAffordance action={block.action}>
        <Box sx={{ ...compactRowSx, width: '100%' }}>
          <Box component="span" sx={{ color: 'text.secondary' }}>
            {block.label}
          </Box>
          <Box component="span" sx={{ fontFamily: COMPACT_MONO_FONT, color: COMPACT_ACCENT, textAlign: 'right' }}>
            {block.value}
            {block.unit && (
              <Box component="span" sx={{ ml: 0.25 }}>
                {block.unit}
              </Box>
            )}
          </Box>
        </Box>
      </ActionAffordance>
    )
  }

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
