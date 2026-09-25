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
  /** FOUND LIVE (2026-09-13): `data-*` attributes (e.g. `RESERVED_ATTRIBUTE` — see
   * `WorkspaceOverlay.tsx`) must land on THIS component's own root, not a wrapper around it.
   * This is the one absolutely-positioned, actually-sized element; a plain `<Box>` wrapping it
   * with no positioning of its own collapses to a 0×0 rect (its only child is out of flow), so
   * a caller marking that wrapper "reserved" was marking a region nothing could ever measure —
   * `useAvoidReservedCorner`/`collectReservedRects` both skip zero-size rects, so the toolbar's
   * own chrome silently never occupied any reserved space at all — the confirmed cause of the
   * viewer's "Solar Analysis" toolbar entry rendering behind the Account Menu button, since the
   * corner-avoidance code had nothing real to measure. */
  dataAttributes?: Record<string, string>
  /** 'anchored' (default) — absolutely positioned at `anchor`, with its own outer margin. 'inline'
   * — laid out in its parent's flow, for when a parent owns the positioning (specs/073 D2: the
   * studio's HUD top bar puts the top-right cluster on the same row as its top-left items). Only
   * the positioning changes; `anchor` still decides wrap direction and alignment. */
  placement?: 'anchored' | 'inline'
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
export function FloatingToolbar({
  anchor,
  direction = 'row',
  sx,
  children,
  dataAttributes,
  placement = 'anchored',
}: FloatingToolbarProps) {
  return (
    <Stack
      direction={direction}
      spacing={1.5}
      useFlexGap
      {...dataAttributes}
      sx={[
        {
          flexWrap: direction === 'row' ? (anchor.startsWith('bottom') ? 'wrap-reverse' : 'wrap') : 'nowrap',
          alignItems: direction === 'row' ? 'flex-start' : anchor.endsWith('end') ? 'flex-end' : 'flex-start',
          justifyContent: anchor.endsWith('end') ? 'flex-end' : 'flex-start',
        },
        placement === 'anchored' && {
          position: 'absolute',
          m: { xs: 2, sm: 3 },
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
