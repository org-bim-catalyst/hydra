import { Box, Stack, Typography } from '@mui/material'
import { blockRegistry } from './blockRegistry'
import { blockSchema, type LooseBlock, type PanelContent } from './blocks'

/** contracts/content-vocabulary.md "Degradation" — every block is parsed against the strict
 * per-block schema individually, right here, not as part of the document's own (loose) gate.
 * That is what lets one bad block show a visible placeholder or error while every sibling still
 * renders (spec User Story 4) — the document already passed the loose envelope gate by the time
 * this component ever sees it (research D11). */
function BlockOutcome({ block }: { block: LooseBlock }) {
  const parsed = blockSchema.safeParse(block)

  if (!parsed.success) {
    const isUnknownKind = !(block.kind in blockRegistry)
    if (isUnknownKind) {
      return (
        <Typography variant="body2" color="text.secondary" sx={{ fontStyle: 'italic' }}>
          Unsupported content &quot;{block.kind}&quot;.
        </Typography>
      )
    }
    return (
      <Box>
        <Typography variant="body2" color="text.secondary">
          This part of the panel couldn&apos;t be displayed.
        </Typography>
        <Typography component="details" variant="caption" color="text.disabled" sx={{ mt: 0.5 }}>
          <Box component="summary" sx={{ cursor: 'pointer' }}>
            Details
          </Box>
          {parsed.error.issues.map((issue) => issue.message).join('; ')}
        </Typography>
      </Box>
    )
  }

  const Renderer = blockRegistry[parsed.data.kind]
  return <Renderer block={parsed.data} />
}

/** Renders an ordered sequence of content blocks (specs/049 User Story 1) — one general
 * presentation for any composition Lucy assembles from the vocabulary, rather than
 * per-composition code. */
export function ContentRenderer({ content }: { content: PanelContent }) {
  return (
    <Stack spacing={1.5}>
      {content.blocks.map((block, index) => (
        // Blocks carry no stable identity of their own — index is the only option, mirroring the
        // same precedent the retired TablePanel used for its rows.
        <BlockOutcome key={index} block={block} />
      ))}
    </Stack>
  )
}
