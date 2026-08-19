import { Box } from '@mui/material'

/** Non-interactive fallback presentation for browsers/devices without WebGL support (FR-005) —
 * a static, `aria-hidden` background, deliberately independent of `features/chat/scene/SceneBackground.tsx`'s
 * own fallback (that file belongs to the separate, unaffected `AiPresenceCard` scene, FR-004). */
export function ViewerFallback() {
  return (
    <Box
      aria-hidden="true"
      data-testid="viewer-fallback"
      sx={{
        position: 'absolute',
        inset: 0,
        backgroundImage: (theme) =>
          theme.palette.mode === 'dark'
            ? 'radial-gradient(circle at 50% 40%, #26241F 0%, #14130F 70%)'
            : 'radial-gradient(circle at 50% 40%, #EEECE5 0%, #DEDBD1 70%)',
      }}
    />
  )
}
