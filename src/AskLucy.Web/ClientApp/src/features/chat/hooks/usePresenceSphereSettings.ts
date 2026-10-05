import { useQuery } from '@tanstack/react-query'
import { useEffect, useMemo } from 'react'
import { getPresenceSphereSettings } from '../api/presenceSphereApi'
import { DEFAULT_SPHERE_LOOK, type PresenceSphereLook } from '../scene/sphereConstants'

export const PRESENCE_SPHERE_SETTINGS_KEY = ['appearance', 'presence-sphere'] as const

/**
 * specs/080: the look the administrator set for the presence sphere. It never makes the sphere wait or
 * disappear: while the settings load, and if they cannot be loaded, the sphere uses its default look. A
 * failure is logged for diagnosis rather than shown, since a decorative card falling back to its default
 * look is not something the user can act on (FR-014).
 */
export function usePresenceSphereSettings(): PresenceSphereLook {
  const query = useQuery({
    queryKey: PRESENCE_SPHERE_SETTINGS_KEY,
    queryFn: getPresenceSphereSettings,
    // Changed rarely, and only picked up on a reload (spec.md Assumptions).
    staleTime: Infinity,
    retry: 1,
  })

  useEffect(() => {
    if (query.error) {
      console.error('Presence sphere settings could not be loaded; using the default look.', query.error)
    }
  }, [query.error])

  const { data } = query
  return useMemo(
    () =>
      data
        ? { dotSizeMultiplier: data.dotSizeMultiplier, cardFillPercent: data.cardFillPercent, zoomEnabled: data.zoomEnabled }
        : DEFAULT_SPHERE_LOOK,
    [data],
  )
}
