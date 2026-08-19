import { Box, Stack } from '@mui/material'
import { featureBlocks } from '../content/copy'
import { flumeriaColor } from '../theme/flumeriaPalette'
import { FeatureBlock } from './FeatureBlock'

/** The five alternating feature blocks (spec.md FR-002), in the reference design's order. */
export function FeatureBlocksSection() {
  return (
    <Box component="section" aria-label="Capabilities" sx={{ bgcolor: flumeriaColor.offWhite }}>
      <Stack spacing={{ xs: 8, md: 12 }} sx={{ px: { xs: 3, sm: 6, md: 10 }, py: { xs: 8, md: 12 } }}>
        {featureBlocks.map(({ key, ...block }) => (
          <FeatureBlock key={key} {...block} />
        ))}
      </Stack>
    </Box>
  )
}
