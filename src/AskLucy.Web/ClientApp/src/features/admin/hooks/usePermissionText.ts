import { useMemo } from 'react'
import type { enAdminRoles } from '../../../i18n/messages/en/admin/roles'
import { useT } from '../../../i18n/useT'
import type { PermissionCatalogEntry } from '../adminPermissions'

type PermissionId = keyof (typeof enAdminRoles)['permissions']
type AreaId = keyof (typeof enAdminRoles)['areas']

/**
 * The catalog id of a permission key: `admin.ai-providers.view` -> `aiProvidersView`. The key itself stays a
 * verbatim identifier everywhere; only its human label is looked up, in the `admin.roles` catalog.
 */
export function permissionTextId(permissionKey: string): PermissionId {
  const [first, ...rest] = permissionKey
    .replace(/^admin\./, '')
    .split(/[.-]/)
    .filter(Boolean)
  return [first, ...rest.map((part) => part[0].toUpperCase() + part.slice(1))].join(
    '',
  ) as PermissionId
}

/** The language-aware area label, display name and description of a permission catalogue entry. */
export function usePermissionText() {
  const t = useT('admin.roles')
  return useMemo(
    () => ({
      area: (entry: PermissionCatalogEntry) => t(`areas.${entry.area as AreaId}`),
      name: (entry: PermissionCatalogEntry) => t(`permissions.${permissionTextId(entry.key)}.name`),
      description: (entry: PermissionCatalogEntry) =>
        t(`permissions.${permissionTextId(entry.key)}.description`),
    }),
    [t],
  )
}
