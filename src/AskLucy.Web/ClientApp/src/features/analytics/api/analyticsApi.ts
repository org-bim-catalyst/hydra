import { apiFetch } from '../../../api/httpClient'

export type FunnelEventType = 'CtaClicked' | 'FunnelCompleted'
export type FunnelCtaId = 'SignIn' | 'SignUp' | 'TryPlatform'
export type FunnelKind = 'SignUp' | 'SignIn'

export interface RecordFunnelEventInput {
  eventType: FunnelEventType
  ctaId?: FunnelCtaId
  funnelType?: FunnelKind
  sessionId: string
  occurredAtUtc: string
}

/** contracts/analytics-funnel-events-api.md — anonymous-allowed, fire-and-forget. */
export function recordFunnelEvent(input: RecordFunnelEventInput) {
  return apiFetch<void>('/analytics/funnel-events', { method: 'POST', body: JSON.stringify(input) })
}
