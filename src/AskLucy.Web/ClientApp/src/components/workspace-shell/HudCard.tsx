import { Box, type SxProps, type Theme } from '@mui/material'
import type { ReactNode } from 'react'
import { CIRCULAR_ACTION_CHROME } from './circularActionChrome'

export interface HudCardProps {
  children: ReactNode
  /** Defaults to `'none'` — a HUD card is decoration over the map, so clicks and drags on it
   * reach the map underneath (specs/073 FR-013). */
  pointerEvents?: 'none' | 'auto'
  maxWidth?: number
  sx?: SxProps<Theme>
  role?: string
  'aria-label'?: string
}

/** specs/073 research D6, contract H1–H3 — the one surface every card in the studio's top-left
 * HUD row shares (the "Flumeria Studio" title, the weather label, the site card): 40 px tall to
 * match the Home `Fab` and the top-right cluster's buttons, so the whole top edge shares one
 * centreline; content laid out in a row and vertically centred. The surface is the workspace
 * chrome's theme callbacks and nothing else — no mode branches, no accent stripe — so every card
 * in the row switches with the light/dark theme together (FR-002a). */
export function HudCard({ children, pointerEvents = 'none', maxWidth, sx, role, 'aria-label': ariaLabel }: HudCardProps) {
  return (
    <Box
      role={role}
      aria-label={ariaLabel}
      sx={[
        {
          display: 'flex',
          alignItems: 'center',
          boxSizing: 'border-box',
          height: 40,
          minWidth: 0,
          maxWidth,
          px: 1.5,
          borderRadius: 2,
          pointerEvents,
          bgcolor: CIRCULAR_ACTION_CHROME.expandedBg,
          border: CIRCULAR_ACTION_CHROME.border,
          backdropFilter: 'blur(12px)',
          color: CIRCULAR_ACTION_CHROME.icon,
          boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
    >
      {children}
    </Box>
  )
}
