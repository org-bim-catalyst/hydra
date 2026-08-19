import { Box } from '@mui/material'

/** The viewer's placeholder content mode (FR-004) — shown before the user's location resolves,
 * or when it's unavailable. Deliberately not a Three.js scene (no camera, no sphere): per the
 * FR-004/FR-013/FR-017 resolution in spec.md, camera view-mode/rotation/orbit-zoom-pan apply
 * only to real content (map/model/overlay layers), never to this placeholder. This is a
 * different element from the existing decorative-sphere presence card (`AiPresenceCard`), which
 * remains a separate, unaffected component (FR-004). */
export function PlaceholderRenderTarget() {
  return (
    <Box
      aria-hidden="true"
      data-testid="viewer-placeholder"
      sx={{
        position: 'absolute',
        inset: 0,
        backgroundImage: (theme) =>
          theme.palette.mode === 'dark'
            ? 'linear-gradient(135deg, #171613 0%, #1F4E5E 45%, #14130F 100%)'
            : 'linear-gradient(135deg, #F7F6F2 0%, #C3BFB1 45%, #EEECE5 100%)',
      }}
    />
  )
}
