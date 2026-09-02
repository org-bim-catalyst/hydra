import MenuOpenIcon from '@mui/icons-material/MenuOpen'
import MenuIcon from '@mui/icons-material/Menu'
import {
  Box,
  Divider,
  IconButton,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Tooltip,
  Typography,
  alpha,
} from '@mui/material'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { Link as RouterLink, useLocation } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ADMIN_NAV } from '../adminNav'
import { overlaySurface } from '../../../theme/tokens/overlaySurface'

const EXPANDED_WIDTH = 232
const COLLAPSED_WIDTH = 60
const STORAGE_KEY = 'ask-lucy.admin-sidebar-collapsed'

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
    <AppShell title={title} subtitle={subtitle} actions={actions}>
      <Box sx={{ display: 'flex', gap: 2, alignItems: 'flex-start', minHeight: 0 }}>
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
            {ADMIN_NAV.map((item) => {
              const selected = pathname === item.path
              return (
                // Each row wrapped in a ListItem so it renders an <li>: ListItemButton with
                // component={RouterLink} is an <a>, and a <ul> may only contain <li> directly.
                <ListItem key={item.path} disablePadding sx={{ display: 'block' }}>
                  <Tooltip title={collapsed ? item.label : ''} placement="right">
                    <ListItemButton
                    component={RouterLink}
                    to={item.path}
                    selected={selected}
                    aria-current={selected ? 'page' : undefined}
                    sx={{
                      borderRadius: `${overlaySurface.itemRadius}px`,
                      mb: 0.25,
                      px: collapsed ? 0 : 1.5,
                      py: 1,
                      justifyContent: collapsed ? 'center' : 'flex-start',
                      '&.Mui-selected': {
                        color: 'primary.main',
                        bgcolor: (t) => alpha(t.palette.primary.main, t.palette.mode === 'dark' ? 0.16 : 0.08),
                      },
                    }}
                  >
                    <ListItemIcon sx={{ minWidth: 0, mr: collapsed ? 0 : 1.5, color: 'inherit' }}>
                      {item.icon}
                    </ListItemIcon>
                    {!collapsed && (
                      <ListItemText
                        primary={item.label}
                        slotProps={{ primary: { sx: { fontSize: '0.875rem', fontWeight: 500 } } }}
                      />
                    )}
                    </ListItemButton>
                  </Tooltip>
                </ListItem>
              )
            })}
          </List>
        </Box>

        <Box sx={{ flex: 1, minWidth: 0 }}>{children}</Box>
      </Box>
    </AppShell>
  )
}
