import AdminPanelSettingsIcon from '@mui/icons-material/AdminPanelSettings'
import ArticleOutlinedIcon from '@mui/icons-material/ArticleOutlined'
import DescriptionIcon from '@mui/icons-material/Description'
import FolderIcon from '@mui/icons-material/Folder'
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined'
import PersonIcon from '@mui/icons-material/Person'
import PolicyIcon from '@mui/icons-material/Policy'
import PsychologyOutlinedIcon from '@mui/icons-material/PsychologyOutlined'
import SettingsIcon from '@mui/icons-material/Settings'
import TuneOutlinedIcon from '@mui/icons-material/TuneOutlined'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import type { ReactNode } from 'react'
import { useIsAdmin } from '../../hooks/useIsAdmin'

export interface AccountMenuItem {
  id: string
  label: string
  icon: ReactNode
  path: string
}

/**
 * The one list of account destinations.
 *
 * There used to be two: `UserMenu`'s and `useAccountControl`'s in the Studio workspace, the
 * second carrying a comment asking whoever changed one to remember the other. That drifted
 * exactly as invited — Studio still offered separate "Chat Configuration" and "Chat History"
 * entries after they had been merged into a single "Chat settings" page everywhere else. Both
 * menus read this now, so a destination added or removed here reaches both by construction.
 */
export function useAccountMenuItems(): AccountMenuItem[] {
  const isAdmin = useIsAdmin()

  return [
    { id: 'profile', label: 'Profile', icon: <PersonIcon fontSize="small" />, path: '/profile' },
    { id: 'settings', label: 'Settings', icon: <SettingsIcon fontSize="small" />, path: '/settings' },
    { id: 'chat-settings', label: 'Chat settings', icon: <TuneOutlinedIcon fontSize="small" />, path: '/chat-settings' },
    { id: 'documents', label: 'Documents', icon: <DescriptionIcon fontSize="small" />, path: '/documents' },
    { id: 'knowledge-bases', label: 'Knowledge Bases', icon: <FolderIcon fontSize="small" />, path: '/knowledge-bases' },
    { id: 'memory', label: 'Memory Center', icon: <PsychologyOutlinedIcon fontSize="small" />, path: '/memory' },
    { id: 'prompts', label: 'Prompts', icon: <ArticleOutlinedIcon fontSize="small" />, path: '/prompts' },
    { id: 'agents', label: 'Agents', icon: <SmartToyOutlinedIcon fontSize="small" />, path: '/agents' },
    { id: 'workflows', label: 'Workflows', icon: <AccountTreeOutlinedIcon fontSize="small" />, path: '/workflows' },
    ...(isAdmin
      ? [
          {
            id: 'admin',
            label: 'Admin panel',
            icon: <AdminPanelSettingsIcon fontSize="small" />,
            path: '/admin/dashboard',
          },
        ]
      : []),
    { id: 'privacy', label: 'Privacy Policy', icon: <PolicyIcon fontSize="small" />, path: '/privacy' },
  ]
}
