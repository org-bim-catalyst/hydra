import { RiCloseLine } from '@remixicon/react'
import { Box, IconButton } from '@mui/material'
import { useEffect, useRef, type ReactNode } from 'react'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'

export interface FloatingPanelProps {
  /** Which `ControlDefinition.id` this panel belongs to — read against
   * `workspaceOverlayStore.expandedControlId` to know when it's open, so `WorkspaceOverlay`
   * doesn't need to thread an extra prop through its generic `content: ReactNode` slot. */
  controlId: string
  /** Accessible label for this panel's `role="region"` (contracts.md's `titleId` —
   * implemented directly as the label text rather than an external id to look up, since
   * nothing else in the tree needs to reference it by id). */
  titleId: string
  onRequestClose: () => void
  children: ReactNode
}

/** Renders inside an expanded `CircularAction` whose `kind` is `'panel'` (today: only
 * `chat`). Visibility/mounting is entirely owned by the ancestor `CircularAction` — its
 * `Collapse` + `inert` wrapper already keeps this mounted while collapsed (no duplicate
 * logic needed here to satisfy that contract guarantee). This component's own job is
 * moving initial focus inside on open, without trapping it (research.md #5), and
 * providing an explicit in-panel close affordance alongside the Escape/outside-click
 * dismissal `CircularAction` already handles. */
export function FloatingPanel({
  controlId,
  titleId,
  onRequestClose,
  children,
}: FloatingPanelProps) {
  const open = useWorkspaceOverlayStore((s) => s.expandedControlId === controlId)
  // Scoped to `children` only — a container ref covering the whole panel (including our
  // own Close button, rendered first in DOM order) would focus Close instead of the
  // first *content* element.
  const contentRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const firstFocusable = contentRef.current?.querySelector<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
    )
    firstFocusable?.focus()
  }, [open])

  return (
    <Box
      role="region"
      aria-label={titleId}
      sx={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        // A bounded (not auto-growing) size — CircularAction's Collapse measures and
        // animates to exactly this, and it gives ConversationView's own internal
        // flex/overflow scrolling a concrete region to scroll within, same as the
        // previous absolutely-positioned AssistantPanel's top/bottom-anchored sizing.
        width: { xs: 'min(92vw, 380px)', sm: 400 },
        height: { xs: 'min(70vh, 600px)', sm: 560 },
        overflow: 'hidden',
        // CircularAction's expanded pill background is a fixed dark chrome color, but
        // this panel's content (AssistantPanel/ConversationView/ChatComposer, etc.)
        // is pre-existing, themed content built assuming a light theme.palette.background
        // surface — carries its own light card here so that content stays readable
        // regardless of the ancestor pill's color.
        bgcolor: 'background.paper',
        color: 'text.primary',
        borderRadius: 2,
      }}
    >
      <IconButton
        onClick={onRequestClose}
        aria-label="Close"
        size="small"
        sx={{ position: 'absolute', top: 4, right: 4, zIndex: 1 }}
      >
        <RiCloseLine size={20} />
      </IconButton>
      <Box
        ref={contentRef}
        sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
      >
        {children}
      </Box>
    </Box>
  )
}
