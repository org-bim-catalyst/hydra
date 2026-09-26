import MenuOpenIcon from '@mui/icons-material/MenuOpen'
import MenuIcon from '@mui/icons-material/Menu'
import {
  Alert,
  Badge,
  Box,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Snackbar,
  Stack,
  Tooltip,
  Typography,
  alpha,
} from '@mui/material'
import type { ReactNode } from 'react'
import type { Theme } from '@mui/material'
import { visuallyHidden } from '@mui/utils'
import { Fragment, useState } from 'react'
import { Link as RouterLink, useLocation } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { AdminSectionActionsContext } from './adminSectionActionsContext'
import { ADMIN_NAV } from '../adminNav'
import { overlaySurface } from '../../../theme/tokens/overlaySurface'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { usePermissions } from '../../auth/hooks/usePermissions'
import { useOpenHangfireDashboard } from '../hooks/useOpenHangfireDashboard'
import { useOperationalFailureBadge } from '../hooks/useOperationalFailureBadge'

const EXPANDED_WIDTH = 232
const COLLAPSED_WIDTH = 60
const STORAGE_KEY = 'ask-lucy.admin-sidebar-collapsed'

/**
 * specs/074 FR-026 — the count on a nav entry's icon, hidden at 0. A failed count is an error
 * dot, never a silent zero. The badge itself is aria-hidden; the visually hidden text says it.
 */
function NavCountBadge({ count, isError, children }: { count: number; isError: boolean; children: ReactNode }) {
  if (isError) {
    return (
      <Tooltip title="Could not load the unacknowledged critical count" describeChild>
        <Badge variant="dot" color="warning" slotProps={{ badge: { 'aria-hidden': true } }}>
          {children}
          <Box component="span" sx={visuallyHidden}>
            (could not load the unacknowledged critical count)
          </Box>
        </Badge>
      </Tooltip>
    )
  }

  return (
    <Badge badgeContent={count} max={99} color="error" invisible={count === 0} slotProps={{ badge: { 'aria-hidden': true } }}>
      {children}
      {count > 0 && (
        <Box component="span" sx={visuallyHidden}>
          ({count} unacknowledged critical)
        </Box>
      )}
    </Badge>
  )
}

interface AdminShellProps {
  title: string
  subtitle?: string
  /** Actions belonging to *this* section — never links to sibling sections. */
  actions?: ReactNode
  children: ReactNode
}

/**
 * The admin panel's frame: a collapsible sidebar of sections beside the active section's own
 * content.
 *
 * Replaces a row of pills that lived in the dashboard's header. That arrangement made the
 * dashboard the only place navigation existed, so every sub-page was a dead end you had to back
 * out of — and two sub-pages had already grown their own partial copies of the row to work
 * around it, each offering a different subset of the destinations.
 *
 * The `actions` slot deliberately stays for a section's *own* controls. It is not for links to
 * other sections; the sidebar is the only place those live now.
 */
export function AdminShell({ title, subtitle, actions, children }: AdminShellProps) {
  const { pathname } = useLocation()
  const isBuiltInAdmin = useIsAdmin()
  const permissions = usePermissions()
  const hangfireDashboard = useOpenHangfireDashboard()
  const operationalFailureBadge = useOperationalFailureBadge()
  const navIcon = (item: (typeof ADMIN_NAV)[number]) =>
    item.badgeKey === 'operationalFailures' ? (
      <NavCountBadge count={operationalFailureBadge.count} isError={operationalFailureBadge.isError}>
        {item.icon}
      </NavCountBadge>
    ) : (
      item.icon
    )
  const visibleNav = ADMIN_NAV.filter((item) => {
    if (isBuiltInAdmin) return true
    if (item.builtInOnly) return false
    if (!item.permission) return true
    const keys = Array.isArray(item.permission) ? item.permission : [item.permission]
    return keys.some((key) => permissions.includes(key))
  }).map((item) => (item.id === 'hangfire-dashboard' ? { ...item, onSelect: hangfireDashboard.open } : item))
  // State rather than a ref: a section's controls portal into this node, and the portal must
  // re-render once the node exists.
  const [actionsSlot, setActionsSlot] = useState<HTMLElement | null>(null)
  const [collapsed, setCollapsed] = useState(() => {
    // Per-browser convenience only, so a failure to read it must never break the page —
    // private windows and blocked site data both throw here.
    try {
      return localStorage.getItem(STORAGE_KEY) === 'true'
    } catch {
      return false
    }
  })

  const toggle = () => {
    setCollapsed((previous) => {
      const next = !previous
      try {
        localStorage.setItem(STORAGE_KEY, String(next))
      } catch {
        // Remembering the choice is a nicety; the toggle itself still works without it.
      }
      return next
    })
  }

  return (
    <AppShell
      title={title}
      subtitle={subtitle}
      actions={
        <Stack ref={setActionsSlot} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          {actions}
        </Stack>
      }
      fillViewport
    >
      <AdminSectionActionsContext.Provider value={actionsSlot}>
      <Box sx={{ display: 'flex', flex: 1, gap: 2, alignItems: 'stretch', minHeight: 0 }}>
        <Box
          component="nav"
          aria-label="Admin sections"
          sx={{
            width: collapsed ? COLLAPSED_WIDTH : EXPANDED_WIDTH,
            flexShrink: 0,
            transition: (t) => t.transitions.create('width', { duration: t.transitions.duration.shorter }),
            borderRadius: `${overlaySurface.panelRadius}px`,
            border: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}`,
            bgcolor: 'background.paper',
            overflow: 'hidden',
            position: 'sticky',
            top: 72,
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: collapsed ? 'center' : 'space-between', px: collapsed ? 0 : 1.5, py: 1 }}>
            {!collapsed && (
              <Typography variant="overline" color="text.secondary" sx={{ letterSpacing: '0.08em' }}>
                Admin
              </Typography>
            )}
            <Tooltip title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}>
              <IconButton
                onClick={toggle}
                size="small"
                aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                aria-expanded={!collapsed}
              >
                {collapsed ? <MenuIcon fontSize="small" /> : <MenuOpenIcon fontSize="small" />}
              </IconButton>
            </Tooltip>
          </Box>
          <Divider />
          <List sx={{ p: 0.75 }}>
            {visibleNav.map((item) => {
              const selected = item.path !== undefined && pathname === item.path
              const itemSx = {
                borderRadius: `${overlaySurface.itemRadius}px`,
                mb: 0.25,
                px: collapsed ? 0 : 1.5,
                py: 1,
                justifyContent: collapsed ? 'center' : 'flex-start',
                '&.Mui-selected': {
                  color: 'primary.main',
                  bgcolor: (t: Theme) => alpha(t.palette.primary.main, t.palette.mode === 'dark' ? 0.16 : 0.08),
                },
              }
              return (
                // Each row wrapped in a ListItem so it renders an <li>: ListItemButton with
                // component={RouterLink} is an <a>, and a <ul> may only contain <li> directly.
                // A dividerAfter entry's <hr> is likewise wrapped in its own <li> — MUI's Divider
                // sets role="separator" on itself whenever it isn't rendered as a bare <hr>, and
                // an element with that role isn't a valid direct child of a <ul> (axe's list
                // rule), so the <hr> must nest inside a plain <li> rather than replace one.
                <Fragment key={item.id ?? item.path}>
                  <ListItem disablePadding sx={{ display: 'block' }}>
                    <Tooltip title={collapsed ? item.label : ''} placement="right">
                      {item.onSelect ? (
                        <ListItemButton onClick={item.onSelect} disabled={hangfireDashboard.isPending} sx={itemSx}>
                          <ListItemIcon sx={{ minWidth: 0, mr: collapsed ? 0 : 1.5, color: 'inherit' }}>
                            {navIcon(item)}
                          </ListItemIcon>
                          {!collapsed && (
                            <ListItemText
                              primary={item.label}
                              slotProps={{ primary: { sx: { fontSize: '0.875rem', fontWeight: 500 } } }}
                            />
                          )}
                        </ListItemButton>
                      ) : (
                        <ListItemButton
                          component={RouterLink}
                          to={item.path ?? ''}
                          selected={selected}
                          aria-current={selected ? 'page' : undefined}
                          sx={itemSx}
                        >
                          <ListItemIcon sx={{ minWidth: 0, mr: collapsed ? 0 : 1.5, color: 'inherit' }}>
                            {navIcon(item)}
                          </ListItemIcon>
                          {!collapsed && (
                            <ListItemText
                              primary={item.label}
                              slotProps={{ primary: { sx: { fontSize: '0.875rem', fontWeight: 500 } } }}
                            />
                          )}
                        </ListItemButton>
                      )}
                    </Tooltip>
                  </ListItem>
                  {item.dividerAfter && (
                    <ListItem component="li" disablePadding sx={{ display: 'block' }}>
                      <Divider sx={{ my: 0.75 }} />
                    </ListItem>
                  )}
                </Fragment>
              )
            })}
          </List>
        </Box>

        <Box sx={{ flex: 1, minWidth: 0, minHeight: 0, display: 'flex', flexDirection: 'column' }}>{children}</Box>
      </Box>
      </AdminSectionActionsContext.Provider>

      <Snackbar open={hangfireDashboard.errorMessage !== null} autoHideDuration={6000} onClose={hangfireDashboard.clearError}>
        <Alert severity="error" variant="filled" onClose={hangfireDashboard.clearError}>
          {hangfireDashboard.errorMessage}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
