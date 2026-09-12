import { Chip, Tooltip } from '@mui/material'
import { useEffect, useState } from 'react'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { useContentStore } from '../../content/contentStore'
import type { ContentFailureReason } from '../../content/ViewerContent'
import type { DrawingRequirement } from '../../scene/rendererState'
import { viewerExtensionRegistry } from '../registry'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'

const CONTENT_FAILURE_MESSAGE: Record<ContentFailureReason, string> = {
  'unsupported-format': 'format not supported',
  'unreachable-or-corrupt': 'could not be loaded',
  unplaceable: 'has no position and cannot be shown',
}

interface DrawingConflict {
  requirement: DrawingRequirement
  requestedBy: string[]
}

interface DrawingCallbackFailure {
  extensionId: string
  message: string
}

/** research D5, D9, FR-029, FR-031, FR-035, FR-037, constitution §2.VIII — the only path any
 * failure this feature (or specs/050) can produce reaches the user through. Reuses the exact Chip
 * treatment `ViewerSurface` already used for the panel hub's `panel-hub-connection-status`
 * indicator rather than inventing a notification system for a failure mode that should never
 * occur in a correct build. Renders nothing when nothing has failed. */
export function ExtensionFailureNotice() {
  const extensions = useViewerExtensionStore((s) => s.extensions)
  const content = useContentStore((s) => s.content)

  // drawingRequirementConflict/drawingCallbackFailed are transient viewerEngine events, not
  // store-backed state — subscribed here directly, mirroring how every other viewer command
  // announces itself (FR-023).
  const [conflicts, setConflicts] = useState<DrawingConflict[]>([])
  const [callbackFailures, setCallbackFailures] = useState<DrawingCallbackFailure[]>([])

  useEffect(() => {
    const unsubscribeConflict = viewerEngine.on('drawingRequirementConflict', ({ requirement, requestedBy }) => {
      setConflicts((prev) => [...prev.filter((c) => c.requirement !== requirement), { requirement, requestedBy }])
    })
    const unsubscribeCallbackFailure = viewerEngine.on('drawingCallbackFailed', ({ extensionId, message }) => {
      setCallbackFailures((prev) => [...prev.filter((f) => f.extensionId !== extensionId), { extensionId, message }])
    })
    return () => {
      unsubscribeConflict()
      unsubscribeCallbackFailure()
    }
  }, [])

  const lifecycleFailures = Object.entries(extensions)
    .filter(([, state]) => state.lifecycle === 'failed')
    .map(([id, state]) => ({
      label: viewerExtensionRegistry.resolve(id)?.manifest.displayName ?? id,
      wording: 'unavailable',
      detail: state.failureReason ?? 'unknown error',
    }))

  const eventFailures = Object.entries(extensions)
    .filter(([, state]) => state.lifecycle !== 'failed' && state.lastEventError !== null)
    .map(([id, state]) => ({
      label: viewerExtensionRegistry.resolve(id)?.manifest.displayName ?? id,
      wording: 'reported an error',
      detail: state.lastEventError ?? 'unknown error',
    }))

  // specs/051 FR-035/FR-037 — every content failure reason worded distinctly.
  const contentFailures = content
    .filter((c) => c.loadState === 'failed')
    .map((c) => ({
      label: 'Content',
      wording: CONTENT_FAILURE_MESSAGE[c.failureReason ?? 'unreachable-or-corrupt'],
      detail: c.failureReason ?? 'unknown error',
    }))

  // specs/051 FR-017 (research D3a) — a drawing capability's onFrame callback threw.
  const drawingCallbackFailureEntries = callbackFailures.map((f) => ({
    label: viewerExtensionRegistry.resolve(f.extensionId)?.manifest.displayName ?? f.extensionId,
    wording: 'reported a drawing error',
    detail: f.message,
  }))

  // specs/051 FR-017 (research D3) — two capabilities declared incompatible drawing requirements.
  const conflictEntries = conflicts.map((c) => ({
    label: `${c.requirement} requirement`,
    wording: 'conflict',
    detail: `requested by ${c.requestedBy.join(', ')}`,
  }))

  const failures = [...lifecycleFailures, ...eventFailures, ...contentFailures, ...drawingCallbackFailureEntries, ...conflictEntries]

  if (failures.length === 0) return null

  const label =
    failures.length === 1
      ? `${failures[0].label} ${failures[0].wording}`
      : `${failures.length} capabilities affected`

  const detail = failures.map((f) => `${f.label} ${f.wording}: ${f.detail}`).join('\n')

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
