import { apiFetch } from '../../../api/httpClient'

export type McpServerTransport = 'StreamableHttp' | 'Stdio'
export type McpAuthenticationType = 'None' | 'ApiKey' | 'BearerToken' | 'OAuth2ClientCredentials'
export type McpServerHealthStatus = 'Healthy' | 'Degraded' | 'Unavailable' | 'AuthenticationFailed' | 'ConfigurationError' | 'Unknown'
export type McpFailureCategory =
  | 'ConnectionFailure'
  | 'AuthenticationFailure'
  | 'AuthorizationFailure'
  | 'Timeout'
  | 'RateLimit'
  | 'InvalidRequest'
  | 'InvalidResponse'
  | 'ServerError'
  | 'ProtocolError'
  | 'CapabilityDiscoveryFailure'
  | 'ServerUnavailable'
export type AgentToolRiskLevel = 'Low' | 'Medium' | 'High' | 'Critical'
export type McpToolActivationStatus = 'PendingReview' | 'Active' | 'Deactivated'
export type McpAuditAction =
  | 'ServerRegistered'
  | 'ServerUpdated'
  | 'ServerEnabled'
  | 'ServerDisabled'
  | 'ServerRemovalBlocked'
  | 'ServerRemoved'
  | 'CredentialRotated'
  | 'CapabilityDiscoveryStarted'
  | 'CapabilityDiscoverySucceeded'
  | 'CapabilityDiscoveryFailed'
  | 'HealthStateChanged'
  | 'ToolActivated'
  | 'ToolDeactivated'
  | 'UnauthorizedAccessAttempted'

/** Mirrors `McpServerDto` (`AskLucy.Application.Mcp`) — never includes credential material (FR-045). */
export interface McpServer {
  id: string
  name: string
  description: string | null
  endpoint: string
  transport: McpServerTransport
  authenticationType: McpAuthenticationType
  requiresUnauthenticatedConfirmation: boolean
  allowInsecureTransport: boolean
  insecureTransportJustification: string | null
  endpointValidationOverride: boolean
  endpointValidationJustification: string | null
  isEnabled: boolean
  ownerUserId: string
  configurationVersion: number
  capabilityRefreshIntervalMinutes: number
  lastHealthCheckAtUtc: string | null
  lastCapabilityDiscoveryAtUtc: string | null
  createdAtUtc: string
  modifiedAtUtc: string | null
}

export interface McpServerHealth {
  mcpServerId: string
  status: McpServerHealthStatus
  failureCategory: McpFailureCategory | null
  detail: string | null
  checkedAtUtc: string
  consecutiveFailureCount: number
}

export interface McpTool {
  id: string
  mcpServerId: string
  namespacedName: string
  toolName: string
  displayName: string
  description: string
  inputSchemaJson: string
  outputSchemaJson: string
  serverDeclaredRiskLevel: AgentToolRiskLevel | null
  effectiveRiskLevel: AgentToolRiskLevel
  requiredPermissions: string[]
  activationStatus: McpToolActivationStatus
  activatedByUserId: string | null
  activatedAtUtc: string | null
  version: string | null
  isAvailable: boolean
}

export interface McpAuditLogEntry {
  id: string
  mcpServerId: string | null
  userId: string
  action: McpAuditAction
  failureCategory: McpFailureCategory | null
  detailsJson: string
  occurredAtUtc: string
}

export interface McpServerReference {
  agentId: string
  toolName: string
}

export interface McpCapabilityRefreshResult {
  wasSuccessful: boolean
  changeSummaryJson: string | null
  toolCount: number
  resourceCount: number
  promptCount: number
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface RegisterMcpServerInput {
  name: string
  description: string | null
  endpoint: string
  transport: McpServerTransport
  authenticationType: McpAuthenticationType
  credential: string | null
  requiresUnauthenticatedConfirmation: boolean
  allowInsecureTransport: boolean
  insecureTransportJustification: string | null
  endpointValidationOverride: boolean
  endpointValidationJustification: string | null
  capabilityRefreshIntervalMinutes: number
}

export type UpdateMcpServerInput = Omit<RegisterMcpServerInput, 'credential'>

export interface ListMcpServersParams {
  status?: McpServerHealthStatus | null
  transport?: McpServerTransport | null
  enabled?: boolean | null
  cursor?: string | null
  pageSize?: number
}

const BASE = '/admin/mcp/servers'

export const listMcpServers = (params: ListMcpServersParams = {}) => {
  const query = new URLSearchParams()
  if (params.status) query.set('status', params.status)
  if (params.transport) query.set('transport', params.transport)
  if (params.enabled !== undefined && params.enabled !== null) query.set('enabled', String(params.enabled))
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.pageSize) query.set('pageSize', String(params.pageSize))

  return apiFetch<PagedResult<McpServer>>(`${BASE}?${query.toString()}`)
}

export const getMcpServer = (id: string) => apiFetch<McpServer>(`${BASE}/${id}`)

export const registerMcpServer = (input: RegisterMcpServerInput) =>
  apiFetch<McpServer>(BASE, { method: 'POST', body: JSON.stringify(input) })

export const updateMcpServer = (id: string, input: UpdateMcpServerInput) =>
  apiFetch<McpServer>(`${BASE}/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const deleteMcpServer = (id: string) => apiFetch<void>(`${BASE}/${id}`, { method: 'DELETE' })

export const enableMcpServer = (id: string) => apiFetch<McpServer>(`${BASE}/${id}/actions/enable`, { method: 'POST' })

export const disableMcpServer = (id: string) => apiFetch<McpServer>(`${BASE}/${id}/actions/disable`, { method: 'POST' })

export const testMcpServerConnection = (id: string) =>
  apiFetch<McpServerHealth>(`${BASE}/${id}/actions/test-connection`, { method: 'POST' })

export const refreshMcpCapabilities = (id: string) =>
  apiFetch<McpCapabilityRefreshResult>(`${BASE}/${id}/actions/refresh-capabilities`, { method: 'POST' })

/** spec.md FR-047 — write-only; the response never echoes the credential back. */
export const rotateMcpServerCredential = (id: string, credential: string) =>
  apiFetch<McpServer>(`${BASE}/${id}/actions/rotate-credential`, { method: 'POST', body: JSON.stringify({ credential }) })

export const getMcpServerHealth = (id: string) => apiFetch<McpServerHealth>(`${BASE}/${id}/health`)

export const listMcpServerReferences = (id: string) => apiFetch<McpServerReference[]>(`${BASE}/${id}/references`)

export const listMcpServerTools = (id: string) => apiFetch<McpTool[]>(`${BASE}/${id}/tools`)

export const listMcpAuditLog = (id: string, cursor?: string | null, pageSize = 50) => {
  const query = new URLSearchParams()
  if (cursor) query.set('cursor', cursor)
  query.set('pageSize', String(pageSize))

  return apiFetch<PagedResult<McpAuditLogEntry>>(`${BASE}/${id}/audit-log?${query.toString()}`)
}

export interface ActivateMcpToolInput {
  effectiveRiskLevelOverride: AgentToolRiskLevel | null
  requiredPermissionsJsonOverride: string | null
}

export const activateMcpTool = (serverId: string, toolId: string, input: ActivateMcpToolInput) =>
  apiFetch<McpTool>(`${BASE}/${serverId}/tools/${toolId}/actions/activate`, { method: 'POST', body: JSON.stringify(input) })

export const deactivateMcpTool = (serverId: string, toolId: string) =>
  apiFetch<McpTool>(`${BASE}/${serverId}/tools/${toolId}/actions/deactivate`, { method: 'POST' })
