import { Box } from '@mui/material'
import type { Rect } from '../types/panel'

export interface LandingPlaceholderProps {
  slot: Rect
}

/** specs/054 FR-005f — the drag-time "ghost" outline shown at the free slot a dragged panel would
 * land in if released now (`viewer/panels/layout/arrangement.ts`'s `findCandidateSlots`/
 * `slotAtPoint`, D6). Purely decorative and non-interactive: it never receives pointer events and
 * carries no accessible role, since the accessible state (the panel being dragged) is already
 * announced by the panel itself — a second, momentary, decorative outline would only add noise for
 * a screen reader user. */
export function LandingPlaceholder({ slot }: LandingPlaceholderProps) {
  return (
    <Box
      aria-hidden
      data-testid="landing-placeholder"
      sx={{
        position: 'absolute',
        left: slot.x,
        top: slot.y,
        width: slot.width,
        height: slot.height,
        borderRadius: 2,
        border: '2px dashed',
        borderColor: 'primary.main',
        bgcolor: (theme) => (theme.palette.mode === 'dark' ? 'rgba(144, 202, 249, 0.12)' : 'rgba(25, 118, 210, 0.08)'),
        pointerEvents: 'none',
        zIndex: 0,
      }}
    />
  )
}
