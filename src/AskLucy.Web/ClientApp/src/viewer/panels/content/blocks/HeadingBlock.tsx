import { Typography } from '@mui/material'
import { usePanelDensity } from '../../chrome/density'
import type { HeadingBlock as HeadingBlockData } from '../blocks'

/** contracts/content-vocabulary.md "heading" block. Text is rendered through MUI `Typography`
 * (React's default escaping) — never interpreted as markup (spec FR-005, constitution §8). In a
 * compact panel a heading is small: 12px for level 1, and a dim 11px section label for level 2. */
export function HeadingBlockRenderer({ block }: { block: HeadingBlockData }) {
  const density = usePanelDensity()
  const isSection = block.level === 2

  return (
    <Typography
      variant={isSection ? 'subtitle1' : 'h6'}
      sx={
        density === 'compact'
          ? {
              fontSize: isSection ? 11 : 12,
              fontWeight: isSection ? 500 : 600,
              lineHeight: 1.4,
              color: isSection ? 'text.secondary' : 'text.primary',
              mb: 0.75,
            }
          : { fontWeight: 600 }
      }
    >
      {block.text}
    </Typography>
  )
}
