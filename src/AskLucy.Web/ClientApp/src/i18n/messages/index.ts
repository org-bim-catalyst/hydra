import { arAdminAgentPolicies } from './ar/admin/agentPolicies'
import { enAdminAgentPolicies } from './en/admin/agentPolicies'
import { arAdminAiCapabilities } from './ar/admin/aiCapabilities'
import { enAdminAiCapabilities } from './en/admin/aiCapabilities'
import { arAdminAiProviders } from './ar/admin/aiProviders'
import { enAdminAiProviders } from './en/admin/aiProviders'
import { arAdminDashboard } from './ar/admin/dashboard'
import { enAdminDashboard } from './en/admin/dashboard'
import { arAdminDefaultModels } from './ar/admin/defaultModels'
import { enAdminDefaultModels } from './en/admin/defaultModels'
import { arAdminJobs } from './ar/admin/jobs'
import { enAdminJobs } from './en/admin/jobs'
import { arAdminMcpServers } from './ar/admin/mcpServers'
import { enAdminMcpServers } from './en/admin/mcpServers'
import { arAdminNotifications } from './ar/admin/notifications'
import { enAdminNotifications } from './en/admin/notifications'
import { arAdminRoleAssignments } from './ar/admin/roleAssignments'
import { enAdminRoleAssignments } from './en/admin/roleAssignments'
import { arAdminRoles } from './ar/admin/roles'
import { enAdminRoles } from './en/admin/roles'
import { arAdminShell } from './ar/admin/shell'
import { enAdminShell } from './en/admin/shell'
import { arAdminSystemAgents } from './ar/admin/systemAgents'
import { enAdminSystemAgents } from './en/admin/systemAgents'
import { arAdminUsers } from './ar/admin/users'
import { enAdminUsers } from './en/admin/users'
import { arAdminWorkflowPolicies } from './ar/admin/workflowPolicies'
import { enAdminWorkflowPolicies } from './en/admin/workflowPolicies'
import { arCommon } from './ar/common'
import { arNotifications } from './ar/notifications'
import { enCommon } from './en/common'
import { enNotifications } from './en/notifications'

/** Every catalog, by language and namespace. A namespace added here is available to `useT` at once. */
export const catalogs = {
  en: {
    common: enCommon,
    notifications: enNotifications,
    'admin.shell': enAdminShell,
    'admin.dashboard': enAdminDashboard,
    'admin.users': enAdminUsers,
    'admin.roles': enAdminRoles,
    'admin.roleAssignments': enAdminRoleAssignments,
    'admin.systemAgents': enAdminSystemAgents,
    'admin.aiProviders': enAdminAiProviders,
    'admin.defaultModels': enAdminDefaultModels,
    'admin.aiCapabilities': enAdminAiCapabilities,
    'admin.agentPolicies': enAdminAgentPolicies,
    'admin.workflowPolicies': enAdminWorkflowPolicies,
    'admin.mcpServers': enAdminMcpServers,
    'admin.jobs': enAdminJobs,
    'admin.notifications': enAdminNotifications,
  },
  ar: {
    common: arCommon,
    notifications: arNotifications,
    'admin.shell': arAdminShell,
    'admin.dashboard': arAdminDashboard,
    'admin.users': arAdminUsers,
    'admin.roles': arAdminRoles,
    'admin.roleAssignments': arAdminRoleAssignments,
    'admin.systemAgents': arAdminSystemAgents,
    'admin.aiProviders': arAdminAiProviders,
    'admin.defaultModels': arAdminDefaultModels,
    'admin.aiCapabilities': arAdminAiCapabilities,
    'admin.agentPolicies': arAdminAgentPolicies,
    'admin.workflowPolicies': arAdminWorkflowPolicies,
    'admin.mcpServers': arAdminMcpServers,
    'admin.jobs': arAdminJobs,
    'admin.notifications': arAdminNotifications,
  },
} as const

export type Namespace = keyof (typeof catalogs)['en']
export type EnglishCatalogs = (typeof catalogs)['en']
