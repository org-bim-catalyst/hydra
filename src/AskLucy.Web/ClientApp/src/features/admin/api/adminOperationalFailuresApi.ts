import { apiFetch } from '../../../api/httpClient'
import type { PagedResult } from './adminApi'

// specs/074 contracts/admin-operational-failures.md → Shapes. Enums arrive as strings.

export type FailureSeverity = 'Warning' | 'Error' | 'Critical'

export type FailureEngine =
  | 'Chat'
  | 'AiProvider'
  | 'Voice'
  | 'Embeddings'
  | 'DocumentProcessing'
  | 'ImageGeneration'
  | 'Agent'
  | 'Workflow'
  | 'Mcp'
  | 'BackgroundJob'
  | 'Access'

export type FailureKind =
  | 'CredentialRejected'
  | 'CredentialUnreadable'
  | 'NotConfigured'
  | 'QuotaExhausted'
  | 'RateLimited'
  | 'UsageRestricted'
  | 'Unavailable'
  | 'RequestInvalid'
  | 'ResponseNotUnderstood'
  | 'UnexpectedError'
  | 'TimedOut'
  | 'DependencyUnreachable'
  | 'ValidationFailed'
  | 'JobFailedAfterRetries'
  | 'SignInRefused'
  | 'TwoFactorRefused'
  | 'AccountLocked'
  | 'AccessDenied'

export type TriageState = 'Open' | 'Acknowledged' | 'Resolved'

/** The list's state filter; `Unresolved` is Open or Acknowledged, and is the default. */
export type IncidentStateFilter = TriageState | 'Unresolved'

/** A user as the trail shows them. An erased user has no id, name or email left. */
export interface UserRef {
  id: string | null
  displayName: string | null
  email: string | null
  status: 'Active' | 'Deleted' | 'Erased'
}

export interface IncidentSubject {
  type: 'Workflow' | 'Agent' | 'Document' | 'McpServer'
  id: string
  label: string | null
  deleted: boolean
}

export interface IncidentSummary {
  id: string
  /** Base64; sent back with a transition as its concurrency token. */
  rowVersion: string
  severity: FailureSeverity
  engine: FailureEngine
  operation: string
  kind: FailureKind
  providerId: string | null
  providerName: string | null
  model: string | null
  subject: IncidentSubject | null
  firstSeenUtc: string
  lastSeenUtc: string
  occurrenceCount: number
  /** Below `occurrenceCount` once the per-incident cap is reached (FR-022). */
  storedOccurrenceCount: number
  distinctUserCount: number
  distinctSourceCount: number
  recoveryCount: number
  latestReason: string
  latestCorrelationId: string
  state: TriageState
  rootCauseKey: string
  /** Other unresolved incidents sharing `rootCauseKey` (FR-026b). */
  relatedOpenCount: number
  isRecurrence: boolean
}

export interface CorrectiveAction {
  text: string
  adminRoute: string | null
  adminAction: 'OpenJobsDashboard' | null
}

export interface ProviderHealth {
  status: 'Unknown' | 'Healthy' | 'Unhealthy'
  failureKind: string | null
  checkedAtUtc: string | null
}

export interface IncidentDetail extends IncidentSummary {
  recurrenceOfIncidentId: string | null
  acknowledged: { by: UserRef; atUtc: string } | null
  resolved: { by: UserRef; atUtc: string; note: string | null } | null
  correctiveAction: CorrectiveAction
  /** The AI provider's latest health check (FR-017); null when the incident names no AI provider. */
  providerHealth: ProviderHealth | null
  /** Up to ten of the most recent distinct users. */
  sampleUsers: UserRef[]
  canManage: boolean
  canViewContent: boolean
}

export interface Occurrence {
  id: string
  occurredAtUtc: string
  severity: FailureSeverity
  kind: FailureKind
  reason: string
  correlationId: string
  isFailover: boolean
  user: UserRef | null
  chat: { id: string; title: string | null; deleted: boolean } | null
  messageId: string | null
  workflow: { id: string; name: string | null; executionId: string | null; nodeId: string | null; deleted: boolean } | null
  document: { id: string; name: string | null; knowledgeBaseId: string | null; deleted: boolean } | null
  agent: { id: string; name: string | null; executionId: string | null; deleted: boolean } | null
  mcpServer: { id: string; name: string | null; deleted: boolean } | null
  jobId: string | null
  /** Access-engine occurrences only. */
  sourceIp: string | null
}

export interface ChatInvestigation {
  chat: {
    id: string
    title: string
    owner: UserRef
    createdAtUtc: string
    lastActivityUtc: string
    messageCount: number
    deleted: boolean
  }
  /** `turnNumber` counts the chat's user messages up to the failed reply; null when the reply is unknown. */
  failurePoints: { occurrenceId: string; turnNumber: number | null; occurredAtUtc: string; messageId: string | null }[]
  /** Null unless the caller holds *View user content* (FR-016b). */
  transcript:
    | { id: string; role: 'system' | 'user' | 'assistant'; createdAtUtc: string; content: string; isFailedTurn: boolean }[]
    | null
}

export interface IncidentFilters {
  from?: string
  to?: string
  state?: IncidentStateFilter
  severity?: FailureSeverity
  engine?: FailureEngine
  provider?: string
  kind?: FailureKind
  userId?: string
  page?: number
  pageSize?: number
}

export const OPERATIONAL_FAILURE_QUERY_KEYS = {
  all: ['admin', 'operational-failures'] as const,
  incidents: (filters: IncidentFilters) => ['admin', 'operational-failures', 'incidents', filters] as const,
  incident: (id: string) => ['admin', 'operational-failures', 'incident', id] as const,
  occurrences: (id: string, page: number, pageSize: number) =>
    ['admin', 'operational-failures', 'incident', id, 'occurrences', { page, pageSize }] as const,
  chatInvestigation: (incidentId: string, chatId: string) =>
    ['admin', 'operational-failures', 'incident', incidentId, 'chat', chatId] as const,
}

const BASE = '/admin/operational-failures'

export const getIncidents = (filters: IncidentFilters) => {
  const query = new URLSearchParams()
  for (const [name, value] of Object.entries(filters)) {
    if (value !== undefined && value !== '') query.set(name, String(value))
  }
  return apiFetch<PagedResult<IncidentSummary>>(`${BASE}/incidents?${query.toString()}`)
}

export const getIncident = (id: string) => apiFetch<IncidentDetail>(`${BASE}/incidents/${id}`)

export const getOccurrences = (id: string, page: number, pageSize: number) =>
  apiFetch<PagedResult<Occurrence>>(`${BASE}/incidents/${id}/occurrences?page=${page}&pageSize=${pageSize}`)

export const getChatInvestigation = (incidentId: string, chatId: string) =>
  apiFetch<ChatInvestigation>(`${BASE}/incidents/${incidentId}/chats/${chatId}`)
