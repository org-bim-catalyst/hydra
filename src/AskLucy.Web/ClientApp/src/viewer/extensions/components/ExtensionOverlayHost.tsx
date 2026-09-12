import { Box } from '@mui/material'
import { Fragment } from 'react'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'

/** contracts/extension-context.md, research D1/D3 — renders every currently contributed overlay
 * by subscribing to `viewerExtensionStore`. Because it renders *from the store* rather than from
 * a one-shot handoff, a contribution made before this host mounts appears exactly like one made
 * after (FR-018, FR-020) — there is no "too early" case to handle separately. Mounted once over
 * the viewer, alongside `FloatingPanelHost` (`ViewerSurface.tsx`). */
export function ExtensionOverlayHost() {
  const contributions = useViewerExtensionStore((s) => s.contributions)
  const overlays = contributions.filter((c) => c.kind === 'overlay')

  const seen = new Map<string, number>()

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }}>
      {overlays.map((contribution) => {
        const ordinal = seen.get(contribution.extensionId) ?? 0
        seen.set(contribution.extensionId, ordinal + 1)
        return (
          <Fragment key={`${contribution.extensionId}:${ordinal}`}>
            <contribution.component />
          </Fragment>
        )
      })}
    </Box>
  )
}
