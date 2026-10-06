import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as preferencesApi from '../api/notificationPreferencesApi'
import type { NotificationPreferenceChange, NotificationPreferences } from '../api/notificationPreferencesApi'

export const NOTIFICATION_PREFERENCES_QUERY_KEY = ['notification-preferences']

export function useNotificationPreferences() {
  return useQuery({
    queryKey: NOTIFICATION_PREFERENCES_QUERY_KEY,
    queryFn: () => preferencesApi.getNotificationPreferences(),
  })
}

/** What the screen shows once `changes` are applied: used to flip the switch before the server answers. */
function applyChanges(preferences: NotificationPreferences, changes: NotificationPreferenceChange[]): NotificationPreferences {
  return {
    categories: preferences.categories.map((category) => ({
      ...category,
      channels: category.channels.map((channel) => {
        const change = changes.find((c) => c.category === category.category && c.channel === channel.channel)
        return change && !channel.locked ? { ...channel, enabled: change.enabled } : channel
      }),
    })),
  }
}

/**
 * The switch flips at once and reverts if the save fails. As with the other notification mutations,
 * the failure is exposed through TanStack Query's own `error`, and the component that calls the hook
 * renders the toast from it (nothing is left unsurfaced).
 */
export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (changes: NotificationPreferenceChange[]) => preferencesApi.updateNotificationPreferences(changes),
    onMutate: async (changes) => {
      await queryClient.cancelQueries({ queryKey: NOTIFICATION_PREFERENCES_QUERY_KEY })
      const previous = queryClient.getQueryData<NotificationPreferences>(NOTIFICATION_PREFERENCES_QUERY_KEY)
      if (previous) {
        queryClient.setQueryData(NOTIFICATION_PREFERENCES_QUERY_KEY, applyChanges(previous, changes))
      }
      return { previous }
    },
    onError: (_error, _changes, context) => {
      if (context?.previous) {
        queryClient.setQueryData(NOTIFICATION_PREFERENCES_QUERY_KEY, context.previous)
      }
    },
    onSuccess: (preferences) => {
      queryClient.setQueryData(NOTIFICATION_PREFERENCES_QUERY_KEY, preferences)
    },
  })
}
