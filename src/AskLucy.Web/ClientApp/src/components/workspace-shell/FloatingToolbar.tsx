import { Stack, type SxProps, type Theme } from '@mui/material'
import type { ReactNode } from 'react'

export type FloatingToolbarAnchor = 'top-start' | 'top-end' | 'bottom-start' | 'bottom-end'

export interface FloatingToolbarProps {
  anchor: FloatingToolbarAnchor
  /** 'row' (default) — a horizontal cluster, e.g. the top-right avatar+theme-toggle
   * pair. 'column' — a vertical stack, e.g. the right-side tool-icon stack (readdy.ai
   * reference: Layers/Analysis stacked below the avatar, not lined up beside it). */
  direction?: 'row' | 'column'
  sx?: SxProps<Theme>
  children: ReactNode
}

const anchorSx: Record<FloatingToolbarAnchor, object> = {
  'top-start': { top: 0, left: 0 },
  'top-end': { top: 0, right: 0 },
  'bottom-start': { bottom: 0, left: 0 },
  'bottom-end': { bottom: 0, right: 0 },
}

/** A cluster of one or more `CircularAction`s docked at a fixed corner of the workspace
 * (FR-020) — purely a positioning primitive, no expand/collapse state of its own. In
 * `row` mode, wraps at narrow viewport widths instead of overlapping (US5) — a
 * `bottom-*` anchor wraps *upward* (`wrap-reverse`), a `top-*` anchor wraps downward,
 * so extra rows always grow away from the screen edge they're anchored to rather than
 * off-screen past it. `column` mode is a single vertical stack, no wrapping. */
export function FloatingToolbar({ anchor, direction = 'row', sx, children }: FloatingToolbarProps) {
  return (
    <Stack
      direction={direction}
      spacing={1.5}
      useFlexGap
      sx={[
        {
          position: 'absolute',
          flexWrap: direction === 'row' ? (anchor.startsWith('bottom') ? 'wrap-reverse' : 'wrap') : 'nowrap',
          m: { xs: 2, sm: 3 },
          alignItems: direction === 'row' ? 'flex-start' : anchor.endsWith('end') ? 'flex-end' : 'flex-start',
          justifyContent: anchor.endsWith('end') ? 'flex-end' : 'flex-start',
          maxWidth: { xs: 'calc(100% - 32px)', sm: 'calc(100% - 48px)' },
          ...anchorSx[anchor],
        },
        ...(Array.isArray(sx) ? sx : [sx]),
      ]}
    >
      {children}
    </Stack>
  )
}
