import type { FailureEngine, FailureKind, FailureSeverity } from '../../api/adminOperationalFailuresApi'

const ENGINE_LABELS: Record<FailureEngine, string> = {
  Chat: 'Chat',
  AiProvider: 'AI provider',
  Voice: 'Voice',
  Embeddings: 'Embeddings',
  DocumentProcessing: 'Document processing',
  ImageGeneration: 'Image generation',
  Agent: 'Agent',
  Workflow: 'Workflow',
  Mcp: 'MCP',
  BackgroundJob: 'Background job',
  Access: 'Access',
}

export const FAILURE_SEVERITIES: FailureSeverity[] = ['Critical', 'Error', 'Warning']

export const FAILURE_ENGINES = Object.keys(ENGINE_LABELS) as FailureEngine[]

export const FAILURE_KINDS: FailureKind[] = [
  'CredentialRejected',
  'CredentialUnreadable',
  'NotConfigured',
  'QuotaExhausted',
  'RateLimited',
  'UsageRestricted',
  'Unavailable',
  'RequestInvalid',
  'ResponseNotUnderstood',
  'UnexpectedError',
  'TimedOut',
  'DependencyUnreachable',
  'ValidationFailed',
  'JobFailedAfterRetries',
  'SignInRefused',
  'TwoFactorRefused',
  'AccountLocked',
  'AccessDenied',
]

export const engineLabel = (engine: FailureEngine) => ENGINE_LABELS[engine] ?? engine

/** `CredentialRejected` → "Credential rejected". */
export const kindLabel = (kind: string) => {
  const words = kind.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

export const severityColor = (severity: FailureSeverity): 'error' | 'warning' | 'default' =>
  severity === 'Critical' ? 'error' : severity === 'Error' ? 'warning' : 'default'

export const formatWhen = (utc: string) => new Date(utc).toLocaleString()

export const usersSearchRoute = (email: string) => `/admin/users?search=${encodeURIComponent(email)}`

export const chatInvestigationRoute = (incidentId: string, chatId: string) =>
  `/admin/operational-failures/${incidentId}/chats/${chatId}`
