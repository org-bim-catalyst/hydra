import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined'
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined'
import HubOutlinedIcon from '@mui/icons-material/HubOutlined'
import ModelTrainingOutlinedIcon from '@mui/icons-material/ModelTrainingOutlined'
import TuneOutlinedIcon from '@mui/icons-material/TuneOutlined'
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import DnsOutlinedIcon from '@mui/icons-material/DnsOutlined'
import type { ReactNode } from 'react'

export interface AdminNavItem {
  path: string
  label: string
  icon: ReactNode
}

/**
 * The admin panel's sections, in one place.
 *
 * These were a row of pills in the dashboard's `actions` slot, which made the dashboard the only
 * way to reach anything and left every sub-page a dead end. Two of those sub-pages had then
 * grown their own partial copies of the row to get around it — Providers offering "Default
 * models" and "Manage capabilities", Default models offering "Manage providers" — so the set of
 * destinations differed depending on where you were standing.
 *
 * One list, rendered once by `AdminShell`, so navigation is identical from every section and no
 * page needs to carry links to its siblings.
 */
export const ADMIN_NAV: AdminNavItem[] = [
  { path: '/admin/dashboard', label: 'Dashboard', icon: <DashboardOutlinedIcon fontSize="small" /> },
  { path: '/admin/users', label: 'Users', icon: <PeopleOutlinedIcon fontSize="small" /> },
  { path: '/admin/ai-providers', label: 'AI providers', icon: <HubOutlinedIcon fontSize="small" /> },
  { path: '/admin/default-models', label: 'Default models', icon: <ModelTrainingOutlinedIcon fontSize="small" /> },
  { path: '/admin/ai-capabilities', label: 'AI capabilities', icon: <TuneOutlinedIcon fontSize="small" /> },
  { path: '/admin/agent-policies', label: 'Agent policies', icon: <SmartToyOutlinedIcon fontSize="small" /> },
  { path: '/admin/workflow-policies', label: 'Workflow policies', icon: <AccountTreeOutlinedIcon fontSize="small" /> },
  { path: '/admin/mcp-servers', label: 'MCP servers', icon: <DnsOutlinedIcon fontSize="small" /> },
]
