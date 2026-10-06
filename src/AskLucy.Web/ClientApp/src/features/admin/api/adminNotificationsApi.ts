import { apiFetch } from '../../../api/httpClient'

// specs/067 US6, contracts/admin-notifications-api.md. Enums arrive as strings. Nothing here is a credential, a token or an unmasked address.

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

export type NotificationChannel = 'InApp' | 'Email'

export type DeliveryStatus =
  | 'Pending'
  | 'Sending'
  | 'Retrying'
  | 'Sent'
  | 'Delivered'
  | 'Skipped'
  | 'Failed'
  | 'DeadLettered'
  | 'Cancelled'
  | 'Expired'

export type ChannelHealth = 'Healthy' | 'Degraded' | 'Unhealthy'

export type RetryRefusal = 'NotFailed' | 'NotificationDeleted' | 'NotificationExpired' | 'RecipientDeleted'

export type AnnouncementKind = 'Maintenance' | 'ServiceDegradation' | 'ImportantAnnouncement'

export type AnnouncementAudience = 'AllActiveUsers' | 'Roles'

export interface NotificationStatistics {
  created: number
  sent: number
  failed: number
  deadLettered: number
  ambiguous: number
  emailSuccessRate: number | null
  averageDeliveryLatencyMs: number | null
  p95DeliveryLatencyMs: number | null
  retries: number
  backlog: { outboxPending: number; deliveriesDue: number; oldestDueAgeSeconds: number }
  unreadNotifications: number
  byCategory: { category: NotificationCategory; created: number; failed: number }[]
  series: { bucketStartUtc: string; created: number; sent: number; failed: number }[]
}

export interface NotificationChannelStatus {
  channel: NotificationChannel
  enabled: boolean
  provider: string
  health: ChannelHealth
  checkedAtUtc: string | null
  detail: string | null
  sendLimitPerMinute: number | null
}

export interface AdminDelivery {
  deliveryId: string
  notificationId: string
  type: string
  category: NotificationCategory
  channel: NotificationChannel
  status: DeliveryStatus
  failureKind: string | null
  failureReason: string | null
  providerResponse: string | null
  attempts: number
  lastAttemptAtUtc: string | null
  nextAttemptAtUtc: string | null
  recipient: { kind: 'User' | 'Address' | 'SupportMailbox'; userId: string | null; displayName: string | null; address: string | null }
  correlationId: string
  retryable: boolean
  notRetryableReason: RetryRefusal | null
}

export interface AdminDeliveryDetail {
  delivery: AdminDelivery
  notification: { title: string | null; createdAtUtc: string; language: string; templateVersionId: string | null }
}

export interface AdminPage<T> {
  items: T[]
  nextCursor: string | null
}

export interface DeliveryFilters {
  status?: DeliveryStatus[]
  channel?: NotificationChannel
  category?: NotificationCategory
  type?: string
  from?: string
  to?: string
}

export interface BulkRetryResult {
  requested: number
  retried: number
  skipped: { deliveryId: string; reason: RetryRefusal }[]
}

export interface AdminAnnouncement {
  id: string
  kind: AnnouncementKind
  title: string
  audience: AnnouncementAudience
  targetRoles: { id: string; name: string }[]
  isCritical: boolean
  endsAtUtc: string | null
  publishedAtUtc: string
  publishedBy: string
  recipientCount: number | null
  fanOutStatus: 'InProgress' | 'Completed'
  emailQueued: number
  emailSent: number
  emailExpired: number
}

export interface PublishAnnouncementInput {
  kind: AnnouncementKind
  title: string
  message: string
  audience: AnnouncementAudience
  targetRoleIds: string[] | null
  isCritical: boolean
  endsAtUtc: string | null
}

export interface PublishedAnnouncement {
  id: string
  estimatedRecipients: number
  emailEstimatedMinutes: number
}

export const ADMIN_NOTIFICATIONS_QUERY_KEY = ['admin', 'notifications']

const query = (params: Record<string, string | number | string[] | undefined>) => {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === '') continue
    for (const item of Array.isArray(value) ? value : [value]) search.append(key, String(item))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const getNotificationStatistics = (range: { from?: string; to?: string } = {}) =>
  apiFetch<NotificationStatistics>(`/admin/notifications/statistics${query(range)}`)

export const getNotificationChannels = () => apiFetch<NotificationChannelStatus[]>('/admin/notifications/channels')

export const getNotificationDeliveries = (filters: DeliveryFilters, cursor?: string, limit = 50) =>
  apiFetch<AdminPage<AdminDelivery>>(`/admin/notifications/deliveries${query({ ...filters, cursor, limit })}`)

export const getNotificationDelivery = (deliveryId: string) =>
  apiFetch<AdminDeliveryDetail>(`/admin/notifications/deliveries/${deliveryId}`)

export const retryNotificationDelivery = (deliveryId: string) =>
  apiFetch<{ deliveryId: string; status: DeliveryStatus }>(`/admin/notifications/deliveries/${deliveryId}/actions/retry`, { method: 'POST' })

export const bulkRetryNotificationDeliveries = (deliveryIds: string[]) =>
  apiFetch<BulkRetryResult>('/admin/notifications/deliveries/actions/retry', {
    method: 'POST',
    body: JSON.stringify({ deliveryIds }),
  })

export const getNotificationAnnouncements = (cursor?: string, limit = 25) =>
  apiFetch<AdminPage<AdminAnnouncement>>(`/admin/notifications/announcements${query({ cursor, limit })}`)

export const publishNotificationAnnouncement = (input: PublishAnnouncementInput) =>
  apiFetch<PublishedAnnouncement>('/admin/notifications/announcements', { method: 'POST', body: JSON.stringify(input) })
