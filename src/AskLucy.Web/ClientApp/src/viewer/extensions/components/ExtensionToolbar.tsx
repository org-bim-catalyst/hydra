import { Paper, Stack, Tooltip, IconButton } from '@mui/material'
import { useRef } from 'react'
import { RESERVED_ATTRIBUTE } from '../../panels/layout/reservedRegions'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import { CORNER_CHROME_ATTRIBUTE, useAvoidReservedCorner } from './useAvoidReservedCorner'

/** contracts/extension-context.md, research D6 — the viewer-embedded toolbar: a surface owned by
 * the viewer's own extensions, distinct from `WorkspaceOverlay`'s page-level controls (which this
 * feature does not touch). Renders every contributed entry in contribution order — the store's
 * append-only `contributions` array already gives a stable order (FR-022) with no separate
 * bookkeeping needed. Renders nothing at all, not an empty frame, when no extension has
 * contributed (FR-023).
 *
 * specs/054 D9: declares itself reserved (so the floating panel system never covers it) and uses
 * `useAvoidReservedCorner` to sit below whatever else — namely `WorkspaceOverlay`'s page-level
 * chrome — already occupies this same top-right corner, replacing a prior hardcoded pixel guess. */
export function ExtensionToolbar() {
  const contributions = useViewerExtensionStore((s) => s.contributions)
  const entries = contributions.filter((c) => c.kind === 'toolbarEntry')
  const paperRef = useRef<HTMLDivElement>(null)
  const top = useAvoidReservedCorner(paperRef)

  if (entries.length === 0) return null

  return (
    <Paper
      ref={paperRef}
      elevation={2}
      {...{ [RESERVED_ATTRIBUTE]: '', [CORNER_CHROME_ATTRIBUTE]: '' }}
      sx={{
        position: 'absolute',
        top,
        right: { xs: 16, sm: 24 },
        zIndex: 2,
        p: 0.5,
        bgcolor: 'background.paper',
      }}
    >
      <Stack direction="row" spacing={0.5}>
        {entries.map(({ entry }) => (
          <Tooltip key={entry.id} title={entry.label}>
            <IconButton size="small" aria-label={entry.label} onClick={entry.onClick}>
              <entry.icon />
            </IconButton>
          </Tooltip>
        ))}
      </Stack>
    </Paper>
  )
}
