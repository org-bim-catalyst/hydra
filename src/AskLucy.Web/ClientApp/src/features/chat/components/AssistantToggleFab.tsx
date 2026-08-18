import CloseIcon from '@mui/icons-material/Close'
import { Badge, Fab } from '@mui/material'
import { useAssistantPanelStore } from '../../../store/assistantPanelStore'
import { LucyPortrait } from '../branding/LucyPortrait'

/** FR-006/FR-016: persistent round toggle for the floating assistant panel — stays
 * visible and reachable regardless of the panel's open/collapsed state, and surfaces a
 * dot indicator when an assistant reply arrived while collapsed. Docked bottom-right so
 * it never overlaps the left-anchored panel's own footprint (FR-005) in either state.
 * Displays Lucy's portrait when collapsed (spec 010-lucy-brand-refresh FR-010) so the
 * closed state reads as "Lucy," not a generic chat icon. */
export function AssistantToggleFab() {
  const isOpen = useAssistantPanelStore((s) => s.isOpen)
  const hasUnread = useAssistantPanelStore((s) => s.hasUnreadWhileCollapsed)
  const toggle = useAssistantPanelStore((s) => s.toggle)

  return (
    <Badge
      color="secondary"
      variant="dot"
      overlap="circular"
      invisible={!hasUnread}
      sx={{
        position: 'absolute',
        zIndex: 3,
        bottom: { xs: 16, sm: 24 },
        right: { xs: 16, sm: 24 },
      }}
    >
      <Fab
        color="primary"
        onClick={toggle}
        aria-label={
          isOpen
            ? 'Collapse Ask Lucy assistant'
            : hasUnread
              ? 'Expand Ask Lucy assistant — new message'
              : 'Expand Ask Lucy assistant'
        }
        aria-expanded={isOpen}
        sx={{
          ...(isOpen ? undefined : { p: 0, overflow: 'hidden' }),
          transition: (theme) => theme.transitions.create('transform'),
          '&:hover': { transform: 'scale(1.05)' },
        }}
      >
        {isOpen ? <CloseIcon /> : <LucyPortrait variant="toggle" alt="Lucy" />}
      </Fab>
    </Badge>
  )
}
