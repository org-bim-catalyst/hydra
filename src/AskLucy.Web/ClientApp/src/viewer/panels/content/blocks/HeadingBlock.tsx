import { Typography } from '@mui/material'
import type { HeadingBlock as HeadingBlockData } from '../blocks'

/** contracts/content-vocabulary.md "heading" block. Text is rendered through MUI `Typography`
 * (React's default escaping) — never interpreted as markup (spec FR-005, constitution §8). */
export function HeadingBlockRenderer({ block }: { block: HeadingBlockData }) {
  return (
    <Typography variant={block.level === 2 ? 'subtitle1' : 'h6'} sx={{ fontWeight: 600 }}>
      {block.text}
    </Typography>
  )
}
