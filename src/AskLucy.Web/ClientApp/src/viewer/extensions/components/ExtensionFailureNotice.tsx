import { Chip, Tooltip } from '@mui/material'
import { viewerExtensionRegistry } from '../registry'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'

/** research D5, FR-029, FR-031, constitution §2.VIII — the only path a lifecycle failure *or* an
 * event-handler failure (FR-017, `ExtensionRuntimeState.lastEventError`) reaches the user through.
 * Reuses the exact Chip treatment `ViewerSurface` already used for the panel hub's
 * `panel-hub-connection-status` indicator rather than inventing a notification system for a
 * failure mode that should never occur in a correct build. Names every currently-failed or
 * currently-erroring capability by its manifest `displayName`; renders nothing when nothing has
 * failed. The two kinds are worded differently ("unavailable" vs. "reported an error") since a
 * lifecycle failure means the capability isn't running at all, while an event-handler failure
 * means it's still running but one of its handlers broke. */
export function ExtensionFailureNotice() {
  const extensions = useViewerExtensionStore((s) => s.extensions)

  const lifecycleFailures = Object.entries(extensions)
    .filter(([, state]) => state.lifecycle === 'failed')
    .map(([id, state]) => ({
      id,
      displayName: viewerExtensionRegistry.resolve(id)?.manifest.displayName ?? id,
      reason: state.failureReason,
      kind: 'unavailable' as const,
    }))

  const eventFailures = Object.entries(extensions)
    .filter(([, state]) => state.lifecycle !== 'failed' && state.lastEventError !== null)
    .map(([id, state]) => ({
      id,
      displayName: viewerExtensionRegistry.resolve(id)?.manifest.displayName ?? id,
      reason: state.lastEventError,
      kind: 'error' as const,
    }))

  const failures = [...lifecycleFailures, ...eventFailures]

  if (failures.length === 0) return null

  const label =
    failures.length === 1
      ? `${failures[0].displayName} ${failures[0].kind === 'unavailable' ? 'unavailable' : 'reported an error'}`
      : `${failures.length} capabilities affected`

  const detail = failures
    .map((f) => `${f.displayName} ${f.kind === 'unavailable' ? 'unavailable' : 'error'}: ${f.reason ?? 'unknown error'}`)
    .join('\n')

  return (
    <Tooltip title={detail}>
      <Chip
        role="status"
        label={label}
        size="small"
        variant="outlined"
        color="warning"
        data-testid="extension-failure-notice"
        sx={{
          position: 'absolute',
          bottom: { xs: 16, sm: 24 },
          right: { xs: 16, sm: 24 },
          bgcolor: 'background.paper',
        }}
      />
    </Tooltip>
  )
}
