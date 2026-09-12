import { Paper, Stack, Tooltip, IconButton } from '@mui/material'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'

/** contracts/extension-context.md, research D6 — the viewer-embedded toolbar: a surface owned by
 * the viewer's own extensions, distinct from `WorkspaceOverlay`'s page-level controls (which this
 * feature does not touch). Renders every contributed entry in contribution order — the store's
 * append-only `contributions` array already gives a stable order (FR-022) with no separate
 * bookkeeping needed. Renders nothing at all, not an empty frame, when no extension has
 * contributed (FR-023). */
export function ExtensionToolbar() {
  const contributions = useViewerExtensionStore((s) => s.contributions)
  const entries = contributions.filter((c) => c.kind === 'toolbarEntry')

  if (entries.length === 0) return null

  return (
    <Paper
      elevation={2}
      sx={{
        position: 'absolute',
        top: { xs: 16, sm: 24 },
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
