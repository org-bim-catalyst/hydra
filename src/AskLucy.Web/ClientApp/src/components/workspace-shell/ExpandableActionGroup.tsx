import { Box, ClickAwayListener, IconButton, List, ListItem, ListItemButton, ListItemIcon, ListItemText, Paper, Stack, Tooltip } from '@mui/material'
import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

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
  /** specs/081 US6: choosing this tool from a group only swaps it into the group button; it runs when that button is pressed. */
  swapOnly?: boolean
  /**
   * specs/081 US6: a group of tools. The button shows `activeToolId`'s icon (else the first tool's). A press runs the
   * shown tool when the group isn't active, and opens the flyout when it is. Right-click, a 400 ms press, the corner
   * mark and Down arrow also open the flyout. Choosing a tool from it calls `onChooseTool`.
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

// specs/081 US6: only one group flyout is open at a time; opening one closes whichever was open.
let closeOpenFlyout: (() => void) | null = null
const FLYOUT_MS = 220

/** specs/081 US6 (T042): a ribbon button for a group of tools. The group's other tools slide out above it as icons. */
function ToolGroupButton({ action }: { action: ExpandableActionGroupAction }) {
  const tools = action.tools ?? []
  const shown = tools.find((tool) => tool.id === action.activeToolId) ?? tools[0]
  const others = tools.filter((tool) => tool.id !== shown?.id)
  // Open, and mounted until the slide-out has finished. `flyoutPlace` pins the strip above the button.
  const [open, setOpen] = useState(false)
  const [mounted, setMounted] = useState(false)
  const [flyoutPlace, setFlyoutPlace] = useState<{ left: number; top: number } | null>(null)
  const pressTimer = useRef<number | undefined>(undefined)
  const pressOpened = useRef(false)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const wrapperRef = useRef<HTMLDivElement>(null)

  // The button's place on screen, centred above it. Read from the live rectangle, never cached. The top edge is used
  // (not `bottom` from window.innerHeight): both `left` and `top` then share getBoundingClientRect's frame, which
  // Chrome's innerHeight does not match.
  const measure = () => {
    const rect = wrapperRef.current?.getBoundingClientRect()
    return rect ? { left: rect.left + rect.width / 2, top: rect.top } : null
  }

  const openFlyout = () => {
    const place = measure()
    if (!place) return
    closeOpenFlyout?.()
    closeOpenFlyout = closeFlyout
    setFlyoutPlace(place)
    setMounted(true)
    setOpen(true)
  }

  // While open, the strip follows the button: the ribbon can still be sliding or the page can scroll or resize
  // after the click, and a place read once at the click would leave the strip where the button used to be.
  useLayoutEffect(() => {
    if (!open) return
    let frame = 0
    const follow = () => {
      const place = measure()
      setFlyoutPlace((current) =>
        place && (!current || current.left !== place.left || current.top !== place.top) ? place : current,
      )
      frame = requestAnimationFrame(follow)
    }
    frame = requestAnimationFrame(follow)
    return () => cancelAnimationFrame(frame)
    // measure only reads refs, so the loop needs to start and stop with `open`.
  }, [open])
  const closeFlyout = () => setOpen(false)
  const cancelPress = () => window.clearTimeout(pressTimer.current)

  const onPointerDown = () => {
    pressOpened.current = false
    cancelPress()
    pressTimer.current = window.setTimeout(() => {
      pressOpened.current = true
      openFlyout()
    }, LONG_PRESS_MS)
  }

  const onClick = () => {
    // A press that opened the flyout should not also run the tool when the pointer is released.
    if (pressOpened.current) return
    if (action.highlighted) openFlyout()
    else shown?.onSelect?.()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      openFlyout()
    }
  }

  const chooseTool = (tool: ExpandableActionGroupAction) => {
    closeFlyout()
    if (!tool.disabled) action.onChooseTool?.(tool.id)
  }

  return (
    <Box ref={wrapperRef} sx={{ position: 'relative', width: 40, height: 40 }}>
      <IconButton
        ref={buttonRef}
        onClick={onClick}
        onPointerDown={onPointerDown}
        onPointerUp={cancelPress}
        onPointerLeave={cancelPress}
        onContextMenu={(event: MouseEvent<HTMLButtonElement>) => {
          event.preventDefault()
          openFlyout()
        }}
        onKeyDown={onKeyDown}
        disabled={shown?.disabled}
        aria-label={shown?.label ?? action.label}
        aria-haspopup="true"
        aria-expanded={open}
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
      {/* The corner mark: a click on it opens the flyout, the same as a right-click or a long press. */}
      <Box
        component="span"
        role="presentation"
        onClick={(event) => {
          event.stopPropagation()
          openFlyout()
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
      {/* specs/081 US6: the flyout is a branch of the group button, rendered in the body so the ribbon's clipping
          cannot hide it. It is fixed above the button and slides out with the parent ribbon's clip-path reveal. */}
      {mounted && flyoutPlace && createPortal(
        <FlyoutStrip
          inProp={open}
          onExited={() => setMounted(false)}
          label={action.label}
          place={flyoutPlace}
          onClickAway={(target) => {
            if (!buttonRef.current?.contains(target)) closeFlyout()
          }}
          onEscape={() => {
            closeFlyout()
            buttonRef.current?.focus()
          }}
        >
          {others.map((tool) => (
            <Tooltip key={tool.id} describeChild title={tool.disabled && tool.disabledReason ? `${tool.label} — ${tool.disabledReason}` : tool.label} placement="left">
              <span>
                <IconButton
                  size="small"
                  aria-label={tool.label}
                  disabled={tool.disabled}
                  onClick={() => chooseTool(tool)}
                  sx={{ width: 40, height: 40 }}
                >
                  {tool.icon}
                </IconButton>
              </span>
            </Tooltip>
          ))}
        </FlyoutStrip>,
        document.body,
      )}
    </Box>
  )
}

/** The flyout strip: revealed with the parent ribbon's clip-path slide, from the trunk outwards. */
function FlyoutStrip({ inProp, onExited, label, place, onClickAway, onEscape, children }: {
  inProp: boolean
  onExited?: () => void
  label: string
  place: { left: number; top: number }
  onClickAway: (target: Node) => void
  onEscape: () => void
  children: ReactNode
}) {
  const [revealed, setRevealed] = useState(false)

  useEffect(() => {
    const frame = requestAnimationFrame(() => setRevealed(inProp))
    if (inProp) return () => cancelAnimationFrame(frame)
    // Closing: keep the strip mounted until its slide-out has finished, then let the Popper unmount it.
    const done = window.setTimeout(() => onExited?.(), FLYOUT_MS + 40)
    return () => {
      cancelAnimationFrame(frame)
      window.clearTimeout(done)
    }
  }, [inProp, onExited])

  return (
    <ClickAwayListener onClickAway={(event) => onClickAway(event.target as Node)}>
    <Paper
      elevation={6}
      role="toolbar"
      aria-label={label}
      onKeyDown={(event) => {
        if (event.key === 'Escape') onEscape()
      }}
      sx={{
        position: 'fixed',
        left: place.left,
        top: place.top,
        // Centred on the button and lifted so its bottom edge sits on the button's top edge.
        transform: 'translate(-50%, -100%)',
        zIndex: 1400,
        p: 0.5,
        // The square bottom corners are where the strip meets the trunk, so the branch reads as joined to it.
        borderRadius: '12px 12px 0 0',
        clipPath: revealed ? 'inset(0 0 0 0 round 12px 12px 0 0)' : 'inset(100% 0 0 0 round 12px 12px 0 0)',
        transition: `clip-path ${FLYOUT_MS}ms cubic-bezier(0.4, 0, 0.2, 1)`,
        backdropFilter: 'blur(12px)',
        bgcolor: (t) => (t.palette.mode === 'dark' ? 'oklch(0.18 0.02 280 / 0.97)' : 'rgba(255,255,255,0.96)'),
        border: (t) => (t.palette.mode === 'dark' ? '1px solid oklch(0.34 0.02 280 / 0.6)' : '1px solid rgba(0,0,0,0.12)'),
        borderBottom: 'none',
      }}
    >
      <Stack spacing={0.5}>{children}</Stack>
    </Paper>
    </ClickAwayListener>
  )
}
