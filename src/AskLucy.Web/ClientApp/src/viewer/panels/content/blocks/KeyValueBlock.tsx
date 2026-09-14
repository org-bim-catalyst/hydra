import { Box, Stack, Typography } from '@mui/material'
import { ActionAffordance } from '../../actions/ActionAffordance'
import { COMPACT_MONO_FONT, compactRowSx } from '../../chrome/compactStyles'
import { usePanelDensity } from '../../chrome/density'
import type { KeyValueBlock as KeyValueBlockData } from '../blocks'

/** contracts/content-vocabulary.md "keyValue" block — absorbs the retired `parameters` panel
 * type. A `null` value renders an explicit "not available" marker rather than an empty cell, so
 * missing detail is shown as missing rather than blank (data-model.md). An item carrying a valid
 * action renders through the shared `ActionAffordance` (specs/049 US2); one without, or with a
 * rejected action, renders as plain content. In a compact panel each item is a row on a hairline
 * divider with a monospace value. */
export function KeyValueBlockRenderer({ block }: { block: KeyValueBlockData }) {
  const density = usePanelDensity()

  if (density === 'compact') {
    return (
      <Box>
        {block.items.map((item, index) => (
          <ActionAffordance key={`${item.label}-${index}`} action={item.action}>
            <Box sx={{ ...compactRowSx, width: '100%' }}>
              <Box component="span" sx={{ color: 'text.secondary' }}>
                {item.label}
              </Box>
              {item.value === null ? (
                <Box component="span" sx={{ color: 'text.disabled', fontStyle: 'italic' }}>
                  Not available
                </Box>
              ) : (
                <Box component="span" sx={{ fontFamily: COMPACT_MONO_FONT, textAlign: 'right' }}>
                  {item.value}
                </Box>
              )}
            </Box>
          </ActionAffordance>
        ))}
      </Box>
    )
  }

  return (
    <Stack spacing={1}>
      {block.items.map((item, index) => (
        <ActionAffordance key={`${item.label}-${index}`} action={item.action}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, width: '100%' }}>
            <Typography variant="body2" color="text.secondary">
              {item.label}
            </Typography>
            <Typography variant="body2" sx={{ textAlign: 'right' }}>
              {item.value === null ? (
                <Typography component="span" variant="body2" color="text.disabled" sx={{ fontStyle: 'italic' }}>
                  Not available
                </Typography>
              ) : (
                item.value
              )}
            </Typography>
          </Box>
        </ActionAffordance>
      ))}
    </Stack>
  )
}
