import LogoutIcon from '@mui/icons-material/Logout'
import {
  Avatar,
  Box,
  Divider,
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import type { ReactElement } from 'react'
import { useNavigate } from 'react-router'
import { useLogout } from '../features/auth/hooks/useAuth'
import { useMyProfile } from '../features/profile/hooks/useProfile'
import { useAccountMenuItems } from './account/useAccountMenuItems'
import { isAccountModalPath } from './account/accountModalPages'
import { useAccountModalStore } from '../store/accountModalStore'
import { radius } from '../theme'
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
 * Studio previously had its own parallel implementation built on `ExpandableActionGroup`,
 * with a comment asking whoever edited one list to remember the other. It drifted. This keeps
 * the Studio card's appearance (an identity header above the destinations) and takes its
 * destinations from `useAccountMenuItems`, so there is nothing left to keep in sync.
 */
export function UserMenu({ renderTrigger }: UserMenuProps) {
  const navigate = useNavigate()
  const { data: profile } = useMyProfile()
  const logout = useLogout()
  const items = useAccountMenuItems()
  const openModal = useAccountModalStore((s) => s.open)
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  const initials = profile?.firstName ? profile.firstName[0].toUpperCase() : (profile?.email?.[0].toUpperCase() ?? '?')
  const displayName = [profile?.firstName, profile?.lastName].filter(Boolean).join(' ')

  const open = Boolean(anchorEl)
  const openMenu = (event: React.MouseEvent<HTMLElement>) => setAnchorEl(event.currentTarget)

  // Account destinations open over the page you are on rather than replacing it. Anything
  // without a modal registered still navigates, so adding a destination never silently does
  // nothing.
  const goTo = (path: string) => {
    setAnchorEl(null)
    if (isAccountModalPath(path)) {
      openModal(path)
    } else {
      navigate(path)
    }
  }

  const handleLogout = () => {
    setAnchorEl(null)
    logout.mutate(undefined, { onSuccess: () => navigate('/', { replace: true }) })
  }

  return (
    <>
      {renderTrigger ? (
        renderTrigger({ onClick: openMenu, open })
      ) : (
        <IconButton onClick={openMenu} aria-label="Account menu" size="small">
          <Avatar sx={{ width: 32, height: 32, fontSize: '0.875rem' }}>{initials}</Avatar>
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
        slotProps={{
          paper: {
            elevation: 8,
            sx: {
              mt: 1,
              minWidth: 232,
              borderRadius: radius.lg,
              overflow: 'hidden',
              // Theme-driven rather than a fixed dark panel, so the card follows light and
              // dark mode like the rest of the app.
              bgcolor: 'background.paper',
              backgroundImage: 'none',
              border: (t) =>
                `1px solid ${t.palette.mode === 'dark' ? 'rgba(255,255,255,0.10)' : 'rgba(0,0,0,0.10)'}`,
            },
          },
        }}
      >
        {/* The identity header the Studio card had and the top-bar menu did not. Not a
            MenuItem: it is not selectable and must not take keyboard focus. */}
        <Box sx={{ px: 2, pt: 1.5, pb: 1.25 }}>
          <Typography variant="subtitle2" sx={{ fontWeight: 700, lineHeight: 1.3 }}>
            {displayName || 'Signed in'}
          </Typography>
          {profile?.email && (
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ display: 'block', wordBreak: 'break-all' }}
            >
              {profile.email}
            </Typography>
          )}
        </Box>
        <Divider />
        {items.map((item) => (
          <MenuItem key={item.id} onClick={() => goTo(item.path)}>
            <ListItemIcon>{item.icon}</ListItemIcon>
            <ListItemText>{item.label}</ListItemText>
          </MenuItem>
        ))}
        <Divider />
        <MenuItem onClick={handleLogout}>
          <ListItemIcon>
            <LogoutIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Log out</ListItemText>
        </MenuItem>
      </Menu>
    </>
  )
}
