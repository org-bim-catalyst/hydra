import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { useCan } from '../../auth/hooks/usePermissions'
import * as api from '../api/adminNotificationsApi'
import type { DeliveryFilters } from '../api/adminNotificationsApi'

const KEY = api.ADMIN_NOTIFICATIONS_QUERY_KEY

/** UX affordance only: the server checks `admin.notifications.manage` on every action. A built-in administrator can always manage. */
export function useCanManageNotifications(): boolean {
  const isBuiltInAdmin = useIsAdmin()
  const hasPermission = useCan('admin.notifications.manage')
  return isBuiltInAdmin || hasPermission
}

export function useNotificationStatistics(range: { from?: string; to?: string }) {
  return useQuery({
    queryKey: [...KEY, 'statistics', range],
    queryFn: () => api.getNotificationStatistics(range),
    placeholderData: keepPreviousData,
  })
}

export function useNotificationChannels() {
  return useQuery({ queryKey: [...KEY, 'channels'], queryFn: () => api.getNotificationChannels() })
}

export function useNotificationDeliveries(filters: DeliveryFilters) {
  return useInfiniteQuery({
    queryKey: [...KEY, 'deliveries', filters],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => api.getNotificationDeliveries(filters, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

export function useNotificationDelivery(deliveryId: string | null) {
  return useQuery({
    queryKey: [...KEY, 'delivery', deliveryId],
    queryFn: () => api.getNotificationDelivery(deliveryId!),
    enabled: deliveryId !== null,
  })
}

/** A retry changes the list, the delivery and the dashboard's counts, so all of them are refetched. */
export function useRetryNotificationDelivery() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (deliveryId: string) => api.retryNotificationDelivery(deliveryId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useBulkRetryNotificationDeliveries() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (deliveryIds: string[]) => api.bulkRetryNotificationDeliveries(deliveryIds),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useNotificationAnnouncements() {
  return useInfiniteQuery({
    queryKey: [...KEY, 'announcements'],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => api.getNotificationAnnouncements(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}

export function usePublishNotificationAnnouncement() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: api.PublishAnnouncementInput) => api.publishNotificationAnnouncement(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: [...KEY, 'announcements'] }),
  })
}
