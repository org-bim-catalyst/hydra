import { alpha, Box, CircularProgress, Typography } from '@mui/material'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'

/**
 * Blurs the viewer with a spinner and a short message while the map rebuilds or restyles: opening and
 * closing the outline editor, a theme or map-style change, the first load. A rebuilt map otherwise
 * flashes blank and jumps; this says what is happening until the new map has drawn.
 */
export function MapTransitionOverlay() {
  const transition = useViewerEngineStore((s) => s.mapTransition)
  if (!transition) return null

  return (
    <Box
      data-testid="map-transition-overlay"
      sx={{
        position: 'absolute',
        inset: 0,
        zIndex: 6,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 2,
        backdropFilter: 'blur(8px)',
        WebkitBackdropFilter: 'blur(8px)',
        bgcolor: (t) => alpha(t.palette.background.default, 0.35),
        // Swallows clicks so nothing is pressed on a map that is being replaced.
        pointerEvents: 'auto',
      }}
    >
      <CircularProgress size={48} thickness={3} aria-hidden />
      <Typography role="status" aria-live="polite" variant="body1" sx={{ fontWeight: 500, color: 'text.primary' }}>
        {transition.message}
      </Typography>
    </Box>
  )
}
