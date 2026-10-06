import { apiFetch } from '../../../api/httpClient'
import type { NotificationCategory } from './notificationsApi'

export type NotificationChannel = 'InApp' | 'Email'

/** Only `Immediate` is offered for now; digests are defined on the server but not available (FR-033). */
export type DeliveryFrequency = 'Immediate' | 'DailyDigest' | 'WeeklyDigest'

export interface NotificationChannelPreference {
  channel: NotificationChannel
  enabled: boolean
  /** Mandatory: the server refuses to turn it off (FR-032), and the screen shows it locked. */
  locked: boolean
}

export interface NotificationCategoryPreference {
  category: NotificationCategory
  channels: NotificationChannelPreference[]
  frequency: DeliveryFrequency
  availableFrequencies: DeliveryFrequency[]
}

export interface NotificationPreferences {
  categories: NotificationCategoryPreference[]
}

export interface NotificationPreferenceChange {
  category: NotificationCategory
  channel: NotificationChannel
  enabled: boolean
}

/** contracts/notifications-api.md GET /users/me/notification-preferences */
export function getNotificationPreferences(): Promise<NotificationPreferences> {
  return apiFetch('/users/me/notification-preferences')
}

/** contracts/notifications-api.md PUT /users/me/notification-preferences — atomic; answers with the full effective preferences. */
export function updateNotificationPreferences(changes: NotificationPreferenceChange[]): Promise<NotificationPreferences> {
  return apiFetch('/users/me/notification-preferences', {
    method: 'PUT',
    body: JSON.stringify({ changes }),
  })
}
