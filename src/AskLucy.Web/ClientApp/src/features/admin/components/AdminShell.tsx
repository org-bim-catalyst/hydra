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
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useFormat, useT } from '../../../i18n/useT'
import { AdminSectionActionsContext } from './adminSectionActionsContext'
import { ADMIN_NAV } from '../adminNav'
import { overlaySurface } from '../../../theme/tokens/overlaySurface'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { usePermissions } from '../../auth/hooks/usePermissions'
import { useOpenHangfireDashboard } from '../hooks/useOpenHangfireDashboard'
import { useOperationalFailureBadge } from '../hooks/useOperationalFailureBadge'
import { useDictationSuspendedBadge } from '../hooks/useDictationSuspendedBadge'

const EXPANDED_WIDTH = 232
const COLLAPSED_WIDTH = 60
const STORAGE_KEY = 'ask-lucy.admin-sidebar-collapsed'

/**
 * specs/074 FR-026 — the count on a nav entry's icon, hidden at 0. A failed count is an error
 * dot, never a silent zero. The badge itself is aria-hidden; the visually hidden text says it.
 */
function NavCountBadge({ count, isError, children }: { count: number; isError: boolean; children: ReactNode }) {
  const t = useT('admin.shell')
  if (isError) {
    return (
      <Tooltip title={t('badges.criticalCountError')} describeChild>
        <Badge variant="dot" color="warning" slotProps={{ badge: { 'aria-hidden': true } }}>
          {children}
          <Box component="span" sx={visuallyHidden}>
            {t('badges.criticalCountErrorHidden')}
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
          {t('badges.criticalCount', { count })}
        </Box>
      )}
    </Badge>
  )
}

/** specs/078 FR-016 — a plain warning dot on Voice while dictation is Suspended; no count to show. */
function NavSuspendedBadge({ suspended, children }: { suspended: boolean; children: ReactNode }) {
  const t = useT('admin.shell')
  return (
    <Badge variant="dot" color="warning" invisible={!suspended} slotProps={{ badge: { 'aria-hidden': true } }}>
      {children}
      {suspended && (
        <Box component="span" sx={visuallyHidden}>
          {t('badges.dictationSuspended')}
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
function AdminShellFrame({ title, subtitle, actions, children }: AdminShellProps) {
  const t = useT('admin.shell')
  const { language } = useFormat()
  const isRtl = language === 'ar'
  const { pathname } = useLocation()
  const isBuiltInAdmin = useIsAdmin()
  const permissions = usePermissions()
  const hangfireDashboard = useOpenHangfireDashboard({
    popupBlocked: t('jobs.popupBlocked'),
    openFailed: t('jobs.openFailed'),
  })
  const operationalFailureBadge = useOperationalFailureBadge()
  const dictationSuspendedBadge = useDictationSuspendedBadge()
  const navIcon = (item: (typeof ADMIN_NAV)[number]) => {
    if (item.badgeKey === 'operationalFailures') {
      return (
        <NavCountBadge count={operationalFailureBadge.count} isError={operationalFailureBadge.isError}>
          {item.icon}
        </NavCountBadge>
      )
    }
    if (item.badgeKey === 'dictationSuspended') {
      return <NavSuspendedBadge suspended={dictationSuspendedBadge.suspended}>{item.icon}</NavSuspendedBadge>
    }
    return item.icon
  }
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
          aria-label={t('sidebar.navLabel')}
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
                {t('sidebar.title')}
              </Typography>
            )}
            <Tooltip title={collapsed ? t('sidebar.expand') : t('sidebar.collapse')}>
              <IconButton
                onClick={toggle}
                size="small"
                aria-label={collapsed ? t('sidebar.expand') : t('sidebar.collapse')}
                aria-expanded={!collapsed}
              >
                {collapsed ? (
                  <MenuIcon fontSize="small" />
                ) : (
                  // The open-menu arrow points toward the sidebar's own edge, so it mirrors with the direction.
                  <MenuOpenIcon fontSize="small" sx={{ transform: isRtl ? 'scaleX(-1)' : undefined }} />
                )}
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
                    <Tooltip title={collapsed ? t(item.labelKey) : ''} placement={isRtl ? 'left' : 'right'}>
                      {item.onSelect ? (
                        <ListItemButton onClick={item.onSelect} disabled={hangfireDashboard.isPending} sx={itemSx}>
                          <ListItemIcon sx={{ minWidth: 0, mr: collapsed ? 0 : 1.5, color: 'inherit' }}>
                            {navIcon(item)}
                          </ListItemIcon>
                          {!collapsed && (
                            <ListItemText
                              primary={t(item.labelKey)}
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
                              primary={t(item.labelKey)}
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

/**
 * The admin panel's frame, in the caller's language and direction (specs/067 FR-046a): the surface sets `<html lang dir>` while the admin
 * area is mounted and restores it on leaving, and mirrors the sidebar, header and every section inside. With localization off, or in
 * English, the surface is a pass-through and this renders exactly as it always did.
 */
export function AdminShell(props: AdminShellProps) {
  return (
    <LocalizedSurface scope="page">
      <AdminShellFrame {...props} />
    </LocalizedSurface>
  )
}
