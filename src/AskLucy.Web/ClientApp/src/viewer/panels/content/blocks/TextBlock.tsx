import { Typography } from '@mui/material'
import { usePanelDensity } from '../../chrome/density'
import type { TextBlock as TextBlockData } from '../blocks'

/** contracts/content-vocabulary.md "text" block. `white-space: pre-wrap` preserves line breaks;
 * content is always plain text — never `dangerouslySetInnerHTML` (spec FR-005, constitution §8:
 * model output is untrusted and must never be interpreted as markup or instruction). In a compact
 * panel it reads as an 11px note. */
export function TextBlockRenderer({ block }: { block: TextBlockData }) {
  const density = usePanelDensity()

  return (
    <Typography
      variant="body2"
      color="text.secondary"
      sx={{ whiteSpace: 'pre-wrap', ...(density === 'compact' && { fontSize: 11, lineHeight: 1.45 }) }}
    >
      {block.text}
    </Typography>
  )
}
