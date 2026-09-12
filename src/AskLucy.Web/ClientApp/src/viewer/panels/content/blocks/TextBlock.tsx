import { Typography } from '@mui/material'
import type { TextBlock as TextBlockData } from '../blocks'

/** contracts/content-vocabulary.md "text" block. `white-space: pre-wrap` preserves line breaks;
 * content is always plain text — never `dangerouslySetInnerHTML` (spec FR-005, constitution §8:
 * model output is untrusted and must never be interpreted as markup or instruction). */
export function TextBlockRenderer({ block }: { block: TextBlockData }) {
  return (
    <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'pre-wrap' }}>
      {block.text}
    </Typography>
  )
}
