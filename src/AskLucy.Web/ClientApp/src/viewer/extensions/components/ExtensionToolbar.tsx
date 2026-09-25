import { Fab, Stack, Tooltip } from '@mui/material'
import { useRef } from 'react'
import { CIRCULAR_BUTTON_SX } from '../../../components/workspace-shell/circularActionChrome'
import { RESERVED_ATTRIBUTE } from '../../panels/layout/reservedRegions'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import type { ToolbarEntry } from '../ViewerExtension'
import { CORNER_CHROME_ATTRIBUTE, useAvoidReservedCorner } from './useAvoidReservedCorner'

const alwaysShown = () => true

/** Entry icons carry no size prop (`ToolbarEntry.icon` is a bare component), so the 20 px the
 * Home and theme buttons use is applied here instead. */
const ICON_SIZE = { width: 20, height: 20 }

/** contracts/extension-context.md, research D6 — the viewer-embedded toolbar: a surface owned by
 * the viewer's own extensions, distinct from `WorkspaceOverlay`'s page-level controls (which this
 * feature does not touch). Renders every contributed entry in contribution order — the store's
 * append-only `contributions` array already gives a stable order (FR-022) with no separate
 * bookkeeping needed. Renders nothing at all, not an empty frame, when no extension has
 * contributed (FR-023).
 *
 * specs/054 D9: declares itself reserved (so the floating panel system never covers it) and uses
 * `useAvoidReservedCorner` to sit below whatever else — namely `WorkspaceOverlay`'s page-level
 * chrome — already occupies this same top-right corner, replacing a prior hardcoded pixel guess.
 *
 * A column of the same circular buttons as that chrome, so the entries read as the continuation
 * of the viewer-tool stack above them rather than a separate card. */
export function ExtensionToolbar() {
  const contributions = useViewerExtensionStore((s) => s.contributions)
  const entries = contributions.filter((c) => c.kind === 'toolbarEntry')
  const stackRef = useRef<HTMLDivElement>(null)
  const top = useAvoidReservedCorner(stackRef)

  if (entries.length === 0) return null

  return (
    <Stack
      ref={stackRef}
      spacing={1}
      {...{ [RESERVED_ATTRIBUTE]: '', [CORNER_CHROME_ATTRIBUTE]: '' }}
      sx={{
        position: 'absolute',
        top,
        right: { xs: 16, sm: 24 },
        zIndex: 2,
      }}
    >
      {entries.map(({ entry }) => (
        <ToolbarEntryButton key={entry.id} entry={entry} />
      ))}
    </Stack>
  )
}

function ToolbarEntryButton({ entry }: { entry: ToolbarEntry }) {
  const isShown = (entry.useIsShown ?? alwaysShown)()
  if (!isShown) return null

  return (
    <Tooltip title={entry.label} placement="left">
      <Fab size="small" aria-label={entry.label} onClick={entry.onClick} sx={{ ...CIRCULAR_BUTTON_SX, '& svg': ICON_SIZE }}>
        <entry.icon />
      </Fab>
    </Tooltip>
  )
}
