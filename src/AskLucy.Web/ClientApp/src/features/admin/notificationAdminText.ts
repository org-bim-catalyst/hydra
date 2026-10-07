import { ApiError } from '../../api/httpClient'
import type { Translate } from '../../i18n/useT'

export type NotificationAdminT = Translate<'admin.notifications'>

const CHANNELS = ['InApp', 'Email'] as const
const CATEGORIES = [
  'Security',
  'Account',
  'Agent',
  'Workflow',
  'Document',
  'KnowledgeBase',
  'Memory',
  'System',
  'Billing',
  'Conversation',
] as const
const STATUSES = [
  'Pending',
  'Sending',
  'Retrying',
  'Sent',
  'Delivered',
  'Skipped',
  'Failed',
  'DeadLettered',
  'Cancelled',
  'Expired',
] as const
const HEALTH = ['Healthy', 'Degraded', 'Unhealthy'] as const

const isOneOf = <K extends string>(keys: readonly K[], value: string): value is K =>
  (keys as readonly string[]).includes(value)

// A value the catalog does not know (a newer server) is shown as returned rather than hidden.
export const channelLabel = (t: NotificationAdminT, value: string) =>
  isOneOf(CHANNELS, value) ? t(`channels.${value}`) : value
export const categoryLabel = (t: NotificationAdminT, value: string) =>
  isOneOf(CATEGORIES, value) ? t(`categories.${value}`) : value
export const statusLabel = (t: NotificationAdminT, value: string) =>
  isOneOf(STATUSES, value) ? t(`statuses.${value}`) : value
export const healthLabel = (t: NotificationAdminT, value: string) =>
  isOneOf(HEALTH, value) ? t(`dashboard.health.${value}`) : value

/** The server's own message (already in the caller's language), or the localized generic one. */
export const errorText = (t: NotificationAdminT, err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : t('errors.generic')

/** The same fields `Date#toLocaleString()` shows, so English reads exactly as it always has. */
export const DATE_TIME: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
  hour: 'numeric',
  minute: 'numeric',
  second: 'numeric',
}

/** The same fields `Date#toLocaleDateString()` shows. */
export const DATE_ONLY: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
}

/** Whole numbers as `String(n)` showed them: no digit grouping, so English is unchanged. */
export const PLAIN_NUMBER: Intl.NumberFormatOptions = { useGrouping: false }
export const ONE_DECIMAL: Intl.NumberFormatOptions = {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
  useGrouping: false,
}
