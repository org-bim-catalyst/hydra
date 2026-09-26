import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined'
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined'
import HubOutlinedIcon from '@mui/icons-material/HubOutlined'
import ModelTrainingOutlinedIcon from '@mui/icons-material/ModelTrainingOutlined'
import TuneOutlinedIcon from '@mui/icons-material/TuneOutlined'
import SmartToyOutlinedIcon from '@mui/icons-material/SmartToyOutlined'
import SupportAgentOutlinedIcon from '@mui/icons-material/SupportAgentOutlined'
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined'
import DnsOutlinedIcon from '@mui/icons-material/DnsOutlined'
import BadgeOutlinedIcon from '@mui/icons-material/BadgeOutlined'
import AssignmentIndOutlinedIcon from '@mui/icons-material/AssignmentIndOutlined'
import WorkOutlineOutlinedIcon from '@mui/icons-material/WorkOutlineOutlined'
import RecordVoiceOverOutlinedIcon from '@mui/icons-material/RecordVoiceOverOutlined'
import ReportProblemOutlinedIcon from '@mui/icons-material/ReportProblemOutlined'
import type { ReactNode } from 'react'

export interface AdminNavItem {
  /** Omitted for an action-triggered entry (see `id`/`onSelect`) — it never routes, so it has no location of its own. */
  path?: string
  /**
   * Stable identity for an action-triggered entry, used by `AdminShell` to attach the actual
   * `onSelect` handler at render time (some handlers need hook state — e.g. mutation/error
   * state — that this static list, evaluated at module load, cannot hold itself).
   */
  id?: string
  label: string
  icon: ReactNode
  /**
   * The permission key(s) that make this section visible (ANY-of) — omitted for a screen
   * reserved to built-in roles regardless of permissions (`builtInOnly`, specs/055-role-
   * management FR-002). `AdminShell` filters against the caller's effective permissions; the
   * server enforces the same gate independently on every request.
   */
  permission?: string | string[]
  builtInOnly?: boolean
  /** specs/062 US5 — renders a horizontal divider immediately after this entry. */
  dividerAfter?: boolean
  /**
   * Renders this entry as a button rather than a `RouterLink` (specs/060-hangfire-dashboard-
   * access) — for a row that triggers an action (mint a session, open a new tab) instead of
   * navigating within the SPA. Mutually exclusive with `path`.
   */
  onSelect?: () => void
  /** specs/074 FR-026 — a live count `AdminShell` resolves and renders on this entry's icon. */
  badgeKey?: 'operationalFailures'
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
  { path: '/admin/dashboard', label: 'Dashboard', icon: <DashboardOutlinedIcon fontSize="small" />, permission: 'admin.dashboard.view' },
  { path: '/admin/users', label: 'Users', icon: <PeopleOutlinedIcon fontSize="small" />, permission: 'admin.users.view' },
  { path: '/admin/roles', label: 'Roles', icon: <BadgeOutlinedIcon fontSize="small" />, builtInOnly: true },
  { path: '/admin/role-assignments', label: 'Role assignments', icon: <AssignmentIndOutlinedIcon fontSize="small" />, builtInOnly: true },
  {
    path: '/admin/system-agents',
    label: 'System agents',
    icon: <SupportAgentOutlinedIcon fontSize="small" />,
    permission: 'admin.system-agents.view',
    dividerAfter: true,
  },
  {
    path: '/admin/ai-providers',
    label: 'AI providers',
    icon: <HubOutlinedIcon fontSize="small" />,
    permission: [
      'admin.ai-providers.view',
      'admin.default-models.view',
      'admin.ai-capabilities.view',
      'admin.custom-models.view',
    ],
  },
  { path: '/admin/default-models', label: 'Default models', icon: <ModelTrainingOutlinedIcon fontSize="small" />, permission: 'admin.default-models.view' },
  { path: '/admin/ai-capabilities', label: 'AI capabilities', icon: <TuneOutlinedIcon fontSize="small" />, permission: 'admin.ai-capabilities.view' },
  { path: '/admin/voice', label: 'Voice', icon: <RecordVoiceOverOutlinedIcon fontSize="small" />, permission: 'admin.ai-providers.view' },
  { path: '/admin/agent-policies', label: 'Agent policies', icon: <SmartToyOutlinedIcon fontSize="small" />, permission: 'admin.agent-policies.view' },
  { path: '/admin/workflow-policies', label: 'Workflow policies', icon: <AccountTreeOutlinedIcon fontSize="small" />, permission: 'admin.workflow-policies.view' },
  { path: '/admin/mcp-servers', label: 'MCP servers', icon: <DnsOutlinedIcon fontSize="small" />, permission: 'admin.mcp-servers.view' },
  {
    path: '/admin/operational-failures',
    label: 'Operational failures',
    icon: <ReportProblemOutlinedIcon fontSize="small" />,
    permission: 'admin.operational-failures.view',
    badgeKey: 'operationalFailures',
  },
  {
    id: 'hangfire-dashboard',
    label: 'Jobs',
    icon: <WorkOutlineOutlinedIcon fontSize="small" />,
    builtInOnly: true,
  },
]
