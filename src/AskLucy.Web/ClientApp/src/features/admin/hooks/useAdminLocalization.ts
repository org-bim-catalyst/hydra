import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { LOCALIZATION_QUERY_KEY } from '../../../i18n/useLocalization'
import * as api from '../api/adminLocalizationApi'
import type { LocalizationSettingsInput } from '../api/adminLocalizationApi'

export function useLocalizationSettings() {
  return useQuery({
    queryKey: api.ADMIN_LOCALIZATION_QUERY_KEY,
    queryFn: api.getLocalizationSettings,
  })
}

/**
 * Saves the settings with the row version they were read at. The change takes effect without a redeploy, so the
 * caller's own language state (`['localization']`) is invalidated too: switching localization off or removing a
 * language changes what this very administrator sees.
 */
export function useUpdateLocalizationSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { rowVersion: string; settings: LocalizationSettingsInput }) =>
      api.updateLocalizationSettings(input.rowVersion, input.settings),
    onSuccess: async (saved) => {
      queryClient.setQueryData(api.ADMIN_LOCALIZATION_QUERY_KEY, saved)
      await queryClient.invalidateQueries({ queryKey: LOCALIZATION_QUERY_KEY })
    },
  })
}
