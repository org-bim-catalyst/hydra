import { apiFetch } from '../../../api/httpClient'

// T066 deviation: the codebase's fetch wrapper (httpClient.ts) is `apiFetch`, not Axios — every
// other feature (documentsApi.ts, workflowsApi.ts, ...) already uses it, so this file matches
// that established convention rather than introducing Axios as a second HTTP client.

/** contracts/notifications-api.md — Billing and Conversation exist in the enum but are omitted until their emitters ship (Phase 4/US2). */
export type NotificationCategory =
  | 'Security'
  | 'Account'
  | 'Agent'
  | 'Workflow'
  | 'Document'
  | 'KnowledgeBase'
  | 'Memory'
  | 'System'
  | 'Billing'
  | 'Conversation'

export type NotificationPriority = 'Low' | 'Normal' | 'High' | 'Critical'

export type NotificationDeliveryStatus = 'Pending' | 'Delivered' | 'Suppressed' | 'Failed'

export type NotificationState = 'all' | 'unread' | 'read'

export interface NotificationAction {
  label: string
  route: string
}

export interface NotificationRelatedItem {
  type: string
  id: string
  /** `false` once the item has been deleted — the client shows "no longer available" instead of navigating. */
  available: boolean
}

export interface NotificationItem {
  id: string
  category: NotificationCategory
  type: string
  /** Plain text — never render as HTML (contracts/notifications-api.md, research R11). */
  title: string
  /** Plain text — never render as HTML (contracts/notifications-api.md, research R11). */
  message: string
  priority: NotificationPriority
  status: NotificationDeliveryStatus
  language: string
  createdAtUtc: string
  readAtUtc: string | null
  expiresAtUtc: string | null
  action: NotificationAction | null
  relatedItem: NotificationRelatedItem | null
}

export interface NotificationDetail extends NotificationItem {
  /** Non-sensitive key/value pairs (FR-013). */
  metadata: Record<string, string>
}

export interface NotificationPage {
  items: NotificationItem[]
  nextCursor: string | null
}

export interface ListNotificationsParams {
  cursor?: string
  limit?: number
  category?: NotificationCategory[]
  state?: NotificationState
}

export function listNotifications(params: ListNotificationsParams = {}): Promise<NotificationPage> {
  const query = new URLSearchParams()
  if (params.cursor) query.set('cursor', params.cursor)
  if (params.limit) query.set('limit', String(params.limit))
  if (params.state) query.set('state', params.state)
  for (const category of params.category ?? []) {
    query.append('category', category)
  }
  const suffix = query.toString()
  return apiFetch(`/notifications${suffix ? `?${suffix}` : ''}`)
}

export interface UnreadCount {
  count: number
}

export function getUnreadCount(): Promise<UnreadCount> {
  return apiFetch('/notifications/unread-count')
}

export function getNotification(id: string): Promise<NotificationDetail> {
  return apiFetch(`/notifications/${id}`)
}

export function markNotificationRead(id: string): Promise<void> {
  return apiFetch(`/notifications/${id}/actions/mark-read`, { method: 'POST' })
}

export function markAllNotificationsRead(category?: NotificationCategory): Promise<{ updated: number }> {
  return apiFetch('/notifications/actions/mark-all-read', {
    method: 'POST',
    body: JSON.stringify(category ? { category } : {}),
  })
}

export function deleteNotification(id: string): Promise<void> {
  return apiFetch(`/notifications/${id}`, { method: 'DELETE' })
}
