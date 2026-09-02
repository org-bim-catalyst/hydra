import LogoutIcon from '@mui/icons-material/Logout'
import { Avatar, Box, IconButton, Menu, MenuItem, Typography, alpha } from '@mui/material'
import type { Theme } from '@mui/material'
import { useState } from 'react'
import type { ReactElement } from 'react'
import { useNavigate } from 'react-router'
import { useLogout } from '../features/auth/hooks/useAuth'
import { useMyProfile } from '../features/profile/hooks/useProfile'
import { useAccountMenuItems } from './account/useAccountMenuItems'
import { overlaySurface } from '../theme/tokens/overlaySurface'
import { zIndex } from '../theme/tokens/zIndex'

interface UserMenuProps {
  /**
   * An alternative trigger, given an onClick and the open state. The Studio workspace passes
   * its circular Fab so the floating cluster keeps its own visual language; everywhere else
   * the default avatar button is used.
   */
  renderTrigger?: (props: { onClick: (event: React.MouseEvent<HTMLElement>) => void; open: boolean }) => ReactElement
}

/**
 * The account menu — one component, used by both the AppShell top bar and the Studio
 * workspace's floating control cluster.
 *
 * Laid out to match the readdy.ai reference's dropdown, taken from its compiled markup rather
 * than judged by eye: a `w-64` card at `rounded-xl` with `shadow-lg`, an identity header
 * (`px-4 py-3.5`) carrying an avatar beside a name and email, then rows inside a `p-1.5` well
 * — each row its own `rounded-lg` hover target rather than a full-bleed strip — and Sign Out
 * below a rule, coloured as the destructive action it is.
 *
 * Colours go through the MUI palette rather than the reference's literal oklch values: that
 * page is light-mode only with a fixed green brand, and hardcoding it would break dark mode.
 */
export function UserMenu({ renderTrigger }: UserMenuProps) {
  const navigate = useNavigate()
  const { data: profile } = useMyProfile()
  const logout = useLogout()
  const items = useAccountMenuItems()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  const initials = profile?.firstName ? profile.firstName[0].toUpperCase() : (profile?.email?.[0].toUpperCase() ?? '?')
  const displayName = [profile?.firstName, profile?.lastName].filter(Boolean).join(' ')

  const open = Boolean(anchorEl)
  const openMenu = (event: React.MouseEvent<HTMLElement>) => setAnchorEl(event.currentTarget)

  const goTo = (path: string) => {
    setAnchorEl(null)
    navigate(path)
  }

  const handleLogout = () => {
    setAnchorEl(null)
    logout.mutate(undefined, { onSuccess: () => navigate('/', { replace: true }) })
  }

  const hoverTint = (t: Theme, color: string) => alpha(color, t.palette.mode === 'dark' ? 0.16 : 0.08)

  // `px-3 py-2.5 gap-3 text-sm font-medium rounded-lg`. The reference's rows tint toward the
  // brand on hover rather than the neutral grey MUI reaches for by default.
  const rowSx = {
    px: 1.5,
    py: 1.25,
    gap: 1.5,
    minHeight: 0,
    borderRadius: `${overlaySurface.itemRadius}px`,
    fontSize: '0.875rem',
    fontWeight: 500,
    '&:hover': {
      color: 'primary.main',
      bgcolor: (t: Theme) => hoverTint(t, t.palette.primary.main),
    },
  } as const

  return (
    <>
      {renderTrigger ? (
        renderTrigger({ onClick: openMenu, open })
      ) : (
        <IconButton onClick={openMenu} aria-label="Account menu" size="small">
          <Avatar sx={{ width: 32, height: 32, fontSize: '0.875rem', bgcolor: 'primary.main' }}>{initials}</Avatar>
        </IconButton>
      )}
      <Menu
        anchorEl={anchorEl}
        open={open}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        // Above MUI's own Fab layer (1050): in the Studio cluster this menu is anchored to a
        // Fab that sits beside two more, and at a lower layer they painted over its top edge.
        sx={{ zIndex: zIndex.dropdown }}
        // `animate-scale-in origin-top-right` — 300ms ease-out, growing out of the corner it
        // is anchored to.
        transitionDuration={overlaySurface.enterDurationMs}
        slotProps={{
          list: { sx: { p: 0 } },
          paper: {
            elevation: 0,
            sx: {
              mt: `${overlaySurface.menuOffset}px`,
              width: overlaySurface.menuWidth,
              borderRadius: `${overlaySurface.panelRadius}px`,
              overflow: 'hidden',
              bgcolor: 'background.paper',
              backgroundImage: 'none',
              border: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}`,
              boxShadow: overlaySurface.menuShadow,
              transformOrigin: 'top right',
            },
          },
        }}
      >
        {/* `px-4 py-3.5 border-b` — avatar, name, email. Not a MenuItem: it is not selectable
            and must not take keyboard focus. */}
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1.5,
            px: 2,
            py: 1.75,
            borderBottom: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}`,
          }}
        >
          <Avatar sx={{ width: 40, height: 40, fontSize: '0.875rem', fontWeight: 600, bgcolor: 'primary.main' }}>
            {initials}
          </Avatar>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="body2" sx={{ fontWeight: 600, lineHeight: 1.3 }} noWrap>
              {displayName || 'Signed in'}
            </Typography>
            {profile?.email && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }} noWrap>
                {profile.email}
              </Typography>
            )}
          </Box>
        </Box>

        {/* `p-1.5` well, so each row's rounded hover sits inset from the card edge. */}
        <Box sx={{ p: 0.75 }}>
          {items.map((item) => (
            <MenuItem key={item.id} onClick={() => goTo(item.path)} sx={rowSx}>
              <Box sx={{ display: 'flex', color: 'text.disabled' }}>{item.icon}</Box>
              {item.label}
            </MenuItem>
          ))}
        </Box>

        <Box sx={{ p: 0.75, borderTop: (t) => `1px solid ${alpha(t.palette.divider, 0.7)}` }}>
          <MenuItem
            onClick={handleLogout}
            sx={{
              ...rowSx,
              color: 'error.main',
              '&:hover': {
                color: 'error.main',
                bgcolor: (t: Theme) => hoverTint(t, t.palette.error.main),
              },
            }}
          >
            <Box sx={{ display: 'flex' }}>
              <LogoutIcon fontSize="small" />
            </Box>
            Log out
          </MenuItem>
        </Box>
      </Menu>
    </>
  )
}
