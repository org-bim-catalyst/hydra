import { useQuery } from '@tanstack/react-query'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { useCan } from '../../auth/hooks/usePermissions'
import { ADMIN_PERMISSIONS } from '../adminPermissions'
import * as operationalFailuresApi from '../api/adminOperationalFailuresApi'

const REFRESH_INTERVAL_MS = 60_000

/**
 * specs/074 FR-026 — the admin nav badge: one per root cause with an unacknowledged Critical
 * incident, refreshed every minute. Every transition invalidates it too, so acknowledging a cause
 * clears its badge entry at once rather than on the next poll.
 *
 * Only fetched for a caller who can open the page. A failed fetch is `isError`, which the nav
 * shows as an error dot rather than a silent zero.
 */
export function useOperationalFailureBadge(): { count: number; isError: boolean } {
  const isBuiltInAdmin = useIsAdmin()
  const canView = useCan(ADMIN_PERMISSIONS.operationalFailuresView)

  const query = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.summary,
    queryFn: operationalFailuresApi.getSummary,
    enabled: isBuiltInAdmin || canView,
    refetchInterval: REFRESH_INTERVAL_MS,
  })

  return { count: query.data?.unacknowledgedCriticalRootCauses ?? 0, isError: query.isError }
}
