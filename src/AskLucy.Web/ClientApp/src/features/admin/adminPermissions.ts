/**
 * Mirrors `AdminPermissionCatalog` (src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs) —
 * kept in sync by hand for now (the catalogue is small and changes rarely; see spec.md's
 * decision that the catalogue is code, not admin-editable data). Single source of truth for
 * every permission key and its display metadata — `PermissionPicker` and the Permissions page
 * both read this rather than keeping their own copy.
 */
export interface PermissionCatalogEntry {
  key: string
  area: string
  areaLabel: string
  level: 'View' | 'Manage'
  displayName: string
  description: string
}

export const ADMIN_PERMISSION_CATALOG: PermissionCatalogEntry[] = [
  { key: 'admin.dashboard.view', area: 'Dashboard', areaLabel: 'Dashboard', level: 'View', displayName: 'View dashboard', description: 'View the admin dashboard overview.' },
  { key: 'admin.users.view', area: 'Users', areaLabel: 'Users', level: 'View', displayName: 'View users', description: 'View user list and details.' },
  { key: 'admin.users.manage', area: 'Users', areaLabel: 'Users', level: 'Manage', displayName: 'Manage users', description: 'Create, edit, suspend, or delete users.' },
  { key: 'admin.ai-providers.view', area: 'AiProviders', areaLabel: 'AI providers', level: 'View', displayName: 'View AI providers', description: 'View registered AI provider configurations.' },
  { key: 'admin.ai-providers.manage', area: 'AiProviders', areaLabel: 'AI providers', level: 'Manage', displayName: 'Manage AI providers', description: 'Add, edit, or remove AI provider credentials.' },
  { key: 'admin.default-models.view', area: 'DefaultModels', areaLabel: 'Default models', level: 'View', displayName: 'View default models', description: "View each provider's default model." },
  { key: 'admin.default-models.manage', area: 'DefaultModels', areaLabel: 'Default models', level: 'Manage', displayName: 'Manage default models', description: "Change any provider's default model." },
  { key: 'admin.ai-capabilities.view', area: 'AiCapabilities', areaLabel: 'AI capabilities', level: 'View', displayName: 'View AI capabilities', description: 'View which provider serves each AI capability.' },
  { key: 'admin.ai-capabilities.manage', area: 'AiCapabilities', areaLabel: 'AI capabilities', level: 'Manage', displayName: 'Manage AI capabilities', description: 'Change which provider serves each AI capability.' },
  { key: 'admin.agent-policies.view', area: 'AgentPolicies', areaLabel: 'Agent policies', level: 'View', displayName: 'View agent policies', description: 'View agent execution policy configurations.' },
  { key: 'admin.agent-policies.manage', area: 'AgentPolicies', areaLabel: 'Agent policies', level: 'Manage', displayName: 'Manage agent policies', description: 'Create or edit agent execution policies.' },
  { key: 'admin.system-agents.view', area: 'SystemAgents', areaLabel: 'System agents', level: 'View', displayName: 'View system agents', description: 'View registered system agents.' },
  { key: 'admin.workflow-policies.view', area: 'WorkflowPolicies', areaLabel: 'Workflow policies', level: 'View', displayName: 'View workflow policies', description: 'View workflow execution policy configurations.' },
  { key: 'admin.workflow-policies.manage', area: 'WorkflowPolicies', areaLabel: 'Workflow policies', level: 'Manage', displayName: 'Manage workflow policies', description: 'Create or edit workflow execution policies.' },
  { key: 'admin.mcp-servers.view', area: 'McpServers', areaLabel: 'MCP servers', level: 'View', displayName: 'View MCP servers', description: 'View registered Model Context Protocol servers.' },
  { key: 'admin.mcp-servers.manage', area: 'McpServers', areaLabel: 'MCP servers', level: 'Manage', displayName: 'Manage MCP servers', description: 'Add, edit, or remove MCP server registrations.' },
  { key: 'admin.custom-models.view', area: 'CustomModels', areaLabel: 'Custom models', level: 'View', displayName: 'View custom models', description: 'View custom model deployments and their progress.' },
  { key: 'admin.custom-models.manage', area: 'CustomModels', areaLabel: 'Custom models', level: 'Manage', displayName: 'Manage custom models', description: 'Deploy models from Hugging Face to the production server, cancel deployments, and change model availability.' },
]

export const ADMIN_PERMISSIONS = {
  dashboardView: 'admin.dashboard.view',
  usersView: 'admin.users.view',
  usersManage: 'admin.users.manage',
  aiProvidersView: 'admin.ai-providers.view',
  aiProvidersManage: 'admin.ai-providers.manage',
  defaultModelsView: 'admin.default-models.view',
  defaultModelsManage: 'admin.default-models.manage',
  aiCapabilitiesView: 'admin.ai-capabilities.view',
  aiCapabilitiesManage: 'admin.ai-capabilities.manage',
  agentPoliciesView: 'admin.agent-policies.view',
  agentPoliciesManage: 'admin.agent-policies.manage',
  systemAgentsView: 'admin.system-agents.view',
  workflowPoliciesView: 'admin.workflow-policies.view',
  workflowPoliciesManage: 'admin.workflow-policies.manage',
  mcpServersView: 'admin.mcp-servers.view',
  mcpServersManage: 'admin.mcp-servers.manage',
  customModelsView: 'admin.custom-models.view',
  customModelsManage: 'admin.custom-models.manage',
} as const

export type AdminPermissionKey = (typeof ADMIN_PERMISSIONS)[keyof typeof ADMIN_PERMISSIONS]
