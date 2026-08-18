import AdminPanelSettingsIcon from '@mui/icons-material/AdminPanelSettings'
import DescriptionIcon from '@mui/icons-material/Description'
import FolderIcon from '@mui/icons-material/Folder'
import LogoutIcon from '@mui/icons-material/Logout'
import PersonIcon from '@mui/icons-material/Person'
import PolicyIcon from '@mui/icons-material/Policy'
import PsychologyOutlinedIcon from '@mui/icons-material/PsychologyOutlined'
import SettingsIcon from '@mui/icons-material/Settings'
import { Avatar, Divider, IconButton, ListItemIcon, ListItemText, Menu, MenuItem } from '@mui/material'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useLogout } from '../features/auth/hooks/useAuth'
import { useMyProfile } from '../features/profile/hooks/useProfile'
import { useIsAdmin } from '../hooks/useIsAdmin'

export function UserMenu() {
  const navigate = useNavigate()
  const { data: profile } = useMyProfile()
  const isAdmin = useIsAdmin()
  const logout = useLogout()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  const initials = profile?.firstName ? profile.firstName[0].toUpperCase() : (profile?.email?.[0].toUpperCase() ?? '?')

  const goTo = (path: string) => {
    setAnchorEl(null)
    navigate(path)
  }

  const handleLogout = () => {
    setAnchorEl(null)
    logout.mutate(undefined, { onSuccess: () => navigate('/login', { replace: true }) })
  }

  return (
    <>
      <IconButton onClick={(e) => setAnchorEl(e.currentTarget)} aria-label="Account menu" size="small">
        <Avatar sx={{ width: 32, height: 32, fontSize: '0.875rem' }}>{initials}</Avatar>
      </IconButton>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={() => setAnchorEl(null)}>
        <MenuItem onClick={() => goTo('/profile')}>
          <ListItemIcon>
            <PersonIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Profile</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => goTo('/settings')}>
          <ListItemIcon>
            <SettingsIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Settings</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => goTo('/documents')}>
          <ListItemIcon>
            <DescriptionIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Documents</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => goTo('/knowledge-bases')}>
          <ListItemIcon>
            <FolderIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Knowledge Bases</ListItemText>
        </MenuItem>
        <MenuItem onClick={() => goTo('/memory')}>
          <ListItemIcon>
            <PsychologyOutlinedIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Memory Center</ListItemText>
        </MenuItem>
        {isAdmin && (
          <MenuItem onClick={() => goTo('/admin/dashboard')}>
            <ListItemIcon>
              <AdminPanelSettingsIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText>Admin panel</ListItemText>
          </MenuItem>
        )}
        <MenuItem onClick={() => goTo('/privacy')}>
          <ListItemIcon>
            <PolicyIcon fontSize="small" />
          </ListItemIcon>
          <ListItemText>Privacy Policy</ListItemText>
        </MenuItem>
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
