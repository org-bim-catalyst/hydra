import { IconButton, List, ListItem, ListItemButton, ListItemIcon, ListItemText, Stack } from '@mui/material'
import type { ReactNode } from 'react'

export interface ExpandableActionGroupAction {
  id: string
  label: string
  icon?: ReactNode
  onSelect?: () => void
  /** Row layout only — visually distinguishes a primary action (readdy.ai reference:
   * Analysis's row ends in a highlighted amber "run" action) from the rest of the row. */
  highlighted?: boolean
}

export interface ExpandableActionGroupProps {
  actions: ExpandableActionGroupAction[]
  /** 'row' — icon-only circular buttons in a horizontal row (readdy.ai's Layers/Analysis
   * pattern); 'list' — icon+label rows, one per line (readdy.ai's account dropdown
   * pattern). Defaults to 'row'. */
  layout?: 'row' | 'list'
}

/** Renders inside an expanded `CircularAction` whose `kind` is `'action-group'`
 * (FR-006). Every action is reachable in DOM order by keyboard (`Tab`) once the parent
 * `CircularAction` is expanded (FR-009). Per readdy.ai's reference design, a
 * not-yet-implemented action is still a real, clickable icon here — its `onSelect`
 * opens a "coming soon" dialog (`useComingSoonStore`) rather than this component
 * rendering any inline placeholder text itself (FR-012 is satisfied at the point of
 * interaction, not by disabling or graying out the entry point). */
export function ExpandableActionGroup({ actions, layout = 'row' }: ExpandableActionGroupProps) {
  if (layout === 'list') {
    return (
      <List dense disablePadding sx={{ minWidth: 220, maxWidth: { xs: 280, sm: 320 } }}>
        {actions.map((action) => (
          // <ul> must directly contain only <li> — ListItemButton alone renders a
          // div[role=button] (axe "list" rule), so it's wrapped in ListItem (renders <li>).
          <ListItem key={action.id} disablePadding>
            <ListItemButton
              onClick={action.onSelect}
              sx={{ borderRadius: 1, px: 1, color: 'inherit' }}
            >
              {action.icon && (
                <ListItemIcon sx={{ minWidth: 32, color: 'inherit', opacity: 0.85 }}>{action.icon}</ListItemIcon>
              )}
              <ListItemText primary={action.label} />
            </ListItemButton>
          </ListItem>
        ))}
      </List>
    )
  }

  return (
    <Stack direction="row" spacing={1}>
      {actions.map((action) => (
        <IconButton
          key={action.id}
          onClick={action.onSelect}
          aria-label={action.label}
          title={action.label}
          size="small"
          sx={{
            width: 40,
            height: 40,
            color: action.highlighted ? '#fff' : 'inherit',
            bgcolor: action.highlighted
              ? '#9C62DE'
              : (t) => t.palette.mode === 'dark' ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)',
            '&:hover': {
              bgcolor: action.highlighted
                ? '#7B43C0'
                : (t) => t.palette.mode === 'dark' ? 'rgba(255,255,255,0.16)' : 'rgba(0,0,0,0.10)',
            },
          }}
        >
          {action.icon}
        </IconButton>
      ))}
    </Stack>
  )
}
