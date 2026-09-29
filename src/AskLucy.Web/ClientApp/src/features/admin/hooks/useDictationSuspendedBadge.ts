import { useQuery } from '@tanstack/react-query'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { useCan } from '../../auth/hooks/usePermissions'
import { ADMIN_PERMISSIONS } from '../adminPermissions'
import * as adminVoiceApi from '../api/adminVoiceApi'

const REFRESH_INTERVAL_MS = 60_000

/**
 * specs/078 FR-016 — the admin nav dot on Voice while dictation is Suspended (its primary or
 * Push-to-Talk engine's Critical failure fell back to the browser built-in). Only fetched for a
 * caller who can open the page.
 */
export function useDictationSuspendedBadge(): { suspended: boolean } {
  const isBuiltInAdmin = useIsAdmin()
  const canView = useCan(ADMIN_PERMISSIONS.aiProvidersView)

  const query = useQuery({
    queryKey: adminVoiceApi.VOICE_QUERY_KEYS.dictation,
    queryFn: adminVoiceApi.getDictationSettings,
    enabled: isBuiltInAdmin || canView,
    refetchInterval: REFRESH_INTERVAL_MS,
  })

  return { suspended: query.data?.state === 'Suspended' }
}
