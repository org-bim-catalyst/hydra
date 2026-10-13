import { Box, IconButton, List, ListItem, ListItemButton, ListItemIcon, ListItemText, Menu, MenuItem, Stack } from '@mui/material'
import { useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'

export interface ExpandableActionGroupAction {
  id: string
  label: string
  icon?: ReactNode
  onSelect?: () => void
  /** Row layout only — visually distinguishes a primary action (readdy.ai reference:
   * Analysis's row ends in a highlighted amber "run" action) from the rest of the row. */
  highlighted?: boolean
  /** Not usable right now. `disabledReason` (shown as the tooltip) says why, so a greyed-out button is never a mystery. */
  disabled?: boolean
  disabledReason?: string
  /**
   * specs/081 US6: a group of tools. The button shows `activeToolId`'s icon (else the first tool's); a click
   * runs that tool when the group isn't active, and opens the menu when it is. Right-click, a 400 ms press
   * and the corner mark also open the menu. Choosing a tool calls `onChooseTool`.
   */
  tools?: ExpandableActionGroupAction[]
  activeToolId?: string
  onChooseTool?: (toolId: string) => void
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
      {actions.map((action) => (action.tools ? <ToolGroupButton key={action.id} action={action} /> : (
        <IconButton
          key={action.id}
          onClick={action.onSelect}
          disabled={action.disabled}
          aria-label={action.label}
          title={action.disabled && action.disabledReason ? `${action.label} — ${action.disabledReason}` : action.label}
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
      )))}
    </Stack>
  )
}

const LONG_PRESS_MS = 400

/** specs/081 US6 (T042): a ribbon button for a group of tools, with a menu of the group's tools. */
function ToolGroupButton({ action }: { action: ExpandableActionGroupAction }) {
  const tools = action.tools ?? []
  const shown = tools.find((tool) => tool.id === action.activeToolId) ?? tools[0]
  const [anchor, setAnchor] = useState<HTMLElement | null>(null)
  const pressTimer = useRef<number | undefined>(undefined)
  const pressOpened = useRef(false)

  const buttonRef = useRef<HTMLButtonElement>(null)
  const openMenu = (element: HTMLElement) => setAnchor(element)
  const cancelPress = () => window.clearTimeout(pressTimer.current)

  const onPointerDown = () => {
    pressOpened.current = false
    cancelPress()
    pressTimer.current = window.setTimeout(() => {
      pressOpened.current = true
      if (buttonRef.current) openMenu(buttonRef.current)
    }, LONG_PRESS_MS)
  }

  const onClick = (event: MouseEvent<HTMLButtonElement>) => {
    // A press that opened the menu should not also run the tool when the pointer is released.
    if (pressOpened.current) return
    if (action.highlighted) openMenu(event.currentTarget)
    else shown?.onSelect?.()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      openMenu(event.currentTarget)
    }
  }

  const chooseTool = (tool: ExpandableActionGroupAction) => {
    setAnchor(null)
    if (!tool.disabled) action.onChooseTool?.(tool.id)
  }

  return (
    <Box sx={{ position: 'relative', width: 40, height: 40 }}>
      <IconButton
        ref={buttonRef}
        onClick={onClick}
        onPointerDown={onPointerDown}
        onPointerUp={cancelPress}
        onPointerLeave={cancelPress}
        onContextMenu={(event) => {
          event.preventDefault()
          openMenu(event.currentTarget)
        }}
        onKeyDown={onKeyDown}
        disabled={shown?.disabled}
        aria-label={shown?.label ?? action.label}
        aria-haspopup="menu"
        aria-expanded={anchor !== null}
        title={shown?.disabled && shown.disabledReason ? `${shown.label} — ${shown.disabledReason}` : (shown?.label ?? action.label)}
        size="small"
        sx={{
          width: 40,
          height: 40,
          color: action.highlighted ? '#fff' : 'inherit',
          bgcolor: action.highlighted ? '#9C62DE' : (t) => (t.palette.mode === 'dark' ? 'rgba(255,255,255,0.08)' : 'rgba(0,0,0,0.06)'),
          '&:hover': { bgcolor: action.highlighted ? '#7B43C0' : (t) => (t.palette.mode === 'dark' ? 'rgba(255,255,255,0.16)' : 'rgba(0,0,0,0.10)') },
        }}
      >
        {shown?.icon}
      </IconButton>
      {/* The corner mark: a click on it opens the menu, the same as a right-click or a long press. */}
      <Box
        component="span"
        role="presentation"
        onClick={(event) => {
          event.stopPropagation()
          if (buttonRef.current) openMenu(buttonRef.current)
        }}
        data-testid="tool-group-corner"
        sx={{
          position: 'absolute',
          right: 2,
          bottom: 2,
          width: 0,
          height: 0,
          borderLeft: '5px solid transparent',
          borderBottom: '5px solid currentColor',
          cursor: 'pointer',
          opacity: 0.7,
          transform: 'rotate(-90deg)',
        }}
      />
      <Menu
        disablePortal
        anchorEl={anchor}
        open={anchor !== null}
        onClose={() => setAnchor(null)}
        slotProps={{ paper: { sx: { minWidth: 200 } } }}
      >
        {tools.map((tool) => (
          <MenuItem
            key={tool.id}
            selected={tool.id === shown?.id}
            disabled={tool.disabled}
            title={tool.disabled && tool.disabledReason ? tool.disabledReason : undefined}
            onClick={() => chooseTool(tool)}
          >
            {tool.icon && <ListItemIcon sx={{ minWidth: 32 }}>{tool.icon}</ListItemIcon>}
            <ListItemText primary={tool.label} />
          </MenuItem>
        ))}
      </Menu>
    </Box>
  )
}
