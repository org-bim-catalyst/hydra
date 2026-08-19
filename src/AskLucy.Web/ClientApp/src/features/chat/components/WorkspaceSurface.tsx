import { Box, keyframes } from '@mui/material'
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'

const drift = keyframes`
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
`

/** FR-003/FR-022: the workspace's full-viewport, persistent visual layer — a soft,
 * slowly alternating gradient reserved for future spatial/model content (research.md
 * #8), not a canvas/WebGL scene. A second three.js canvas here would cost render budget
 * for a surface with no interactive purpose yet (constitution §15); the AI presence
 * lives in its own separate `AiPresenceCard` instead (FR-023).
 *
 * FR-011: the active 2D/3D view mode is reflected via a subtle gradient-angle change —
 * this feature does not implement real spatial rendering (FR-021), only the visible
 * acknowledgment that the mode switch took effect. */
export function WorkspaceSurface() {
  const prefersReducedMotion = usePrefersReducedMotion()
  const viewMode = useWorkspaceOverlayStore((s) => s.viewMode)

  return (
    <Box
      aria-hidden="true"
      data-view-mode={viewMode}
      sx={{
        position: 'absolute',
        inset: 0,
        zIndex: 0,
        backgroundImage: (theme) =>
          theme.palette.mode === 'dark'
            ? viewMode === '3D'
              ? 'linear-gradient(135deg, #171613 0%, #1F4E5E 45%, #14130F 100%)'
              : 'linear-gradient(160deg, #171613 0%, #26241F 55%, #14130F 100%)'
            : viewMode === '3D'
              ? 'linear-gradient(135deg, #F7F6F2 0%, #C3BFB1 45%, #EEECE5 100%)'
              : 'linear-gradient(160deg, #F7F6F2 0%, #DEDBD1 55%, #EEECE5 100%)',
        backgroundSize: '200% 200%',
        animation: prefersReducedMotion ? 'none' : `${drift} 24s ease-in-out infinite`,
      }}
    />
  )
}
