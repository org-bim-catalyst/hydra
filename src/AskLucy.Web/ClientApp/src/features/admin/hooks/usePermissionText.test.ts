import { describe, expect, it } from 'vitest'
import { catalogs } from '../../../i18n/messages'
import { ADMIN_PERMISSION_CATALOG } from '../adminPermissions'
import { permissionTextId } from './usePermissionText'

const en = catalogs.en['admin.roles']
const ar = catalogs.ar['admin.roles']

describe('permission catalogue text', () => {
  it('maps permission keys to camel-case catalog ids', () => {
    expect(permissionTextId('admin.ai-providers.view')).toBe('aiProvidersView')
    expect(permissionTextId('admin.operational-failures.content.view')).toBe(
      'operationalFailuresContentView',
    )
  })

  it('has English text identical to the catalogue, and Arabic text, for every permission and area', () => {
    for (const entry of ADMIN_PERMISSION_CATALOG) {
      const id = permissionTextId(entry.key)
      const english = (en.permissions as Record<string, { name: string; description: string }>)[id]
      const arabic = (ar.permissions as Record<string, { name: string; description: string }>)[id]
      expect(english, entry.key).toEqual({
        name: entry.displayName,
        description: entry.description,
      })
      expect(arabic.name.length, entry.key).toBeGreaterThan(0)
      expect((en.areas as Record<string, string>)[entry.area], entry.area).toBe(entry.areaLabel)
      expect((ar.areas as Record<string, string>)[entry.area], entry.area).toBeTruthy()
    }
  })
})
