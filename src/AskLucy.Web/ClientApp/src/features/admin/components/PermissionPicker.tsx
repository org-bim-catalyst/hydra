import { Box, Checkbox, FormControlLabel, Stack, Tooltip, Typography } from '@mui/material'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { ADMIN_PERMISSION_CATALOG, SUPER_USER_CONTROLLED_KEYS, type PermissionCatalogEntry } from '../adminPermissions'

export const SUPER_USER_ONLY_HINT = 'Only a Super User can grant this'
export const BASIC_PERMISSION_HINT = "A basic permission — it can't be removed from this role"

/** The first entry of a level in its area — the one Manage depends on, and the one labelled by level alone. */
function primaryKeyFor(area: string, level: PermissionCatalogEntry['level']): string | null {
  return ADMIN_PERMISSION_CATALOG.find((p) => p.area === area && p.level === level)?.key ?? null
}

function viewKeyFor(entry: PermissionCatalogEntry): string | null {
  if (entry.level !== 'Manage') return null
  return primaryKeyFor(entry.area, 'View')
}

interface PermissionPickerProps {
  selectedKeys: string[]
  onChange: (keys: string[]) => void
  disabled?: boolean
  /** Ticked and can't be unticked — the User role's basic permissions. */
  lockedKeys?: readonly string[]
  /** Not offered at all — e.g. View user content on the role every account holds. */
  hiddenKeys?: ReadonlySet<string>
}

/**
 * Grouped-by-area checkbox picker (FR-004): selecting Manage auto-selects its area's View.
 * Super-User-controlled keys (specs/074 FR-016f) stay visible to everyone but only a Super User
 * can tick or untick them; the server refuses the change regardless.
 */
export function PermissionPicker({ selectedKeys, onChange, disabled, lockedKeys = [], hiddenKeys }: PermissionPickerProps) {
  const isSuperUser = useIsSuperUser()
  const selected = new Set(selectedKeys)
  const basic = new Set(lockedKeys)

  const catalog = ADMIN_PERMISSION_CATALOG.filter((p) => !hiddenKeys?.has(p.key))
  const areas = [...new Set(catalog.map((p) => p.area))].map((area) => ({
    area,
    label: catalog.find((p) => p.area === area)!.areaLabel,
    entries: catalog.filter((p) => p.area === area),
  }))

  const toggle = (entry: PermissionCatalogEntry) => {
    const next = new Set(selected)
    if (next.has(entry.key)) {
      next.delete(entry.key)
      // Removing the area's View also removes its Manage — a role can't manage what it can't view.
      if (entry.key === primaryKeyFor(entry.area, 'View')) {
        const manageKey = primaryKeyFor(entry.area, 'Manage')
        if (manageKey && !basic.has(manageKey)) next.delete(manageKey)
      }
    } else {
      next.add(entry.key)
      const viewKey = viewKeyFor(entry)
      if (viewKey) next.add(viewKey)
    }
    onChange([...next])
  }

  return (
    <Stack spacing={1.5} sx={{ mt: 1 }}>
      {areas.map(({ area, label, entries }) => (
        <Box key={area}>
          <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
            {label}
          </Typography>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
            {entries.map((entry) => {
              const isBasic = basic.has(entry.key)
              const superUserOnly = !isSuperUser && SUPER_USER_CONTROLLED_KEYS.has(entry.key)
              const locked = isBasic || superUserOnly
              const control = (
                <FormControlLabel
                  key={entry.key}
                  control={
                    <Checkbox
                      size="small"
                      checked={isBasic || selected.has(entry.key)}
                      onChange={() => toggle(entry)}
                      disabled={disabled || locked}
                    />
                  }
                  label={entry.key === primaryKeyFor(entry.area, entry.level) ? entry.level : entry.displayName}
                  title={locked ? undefined : entry.description}
                />
              )

              return locked ? (
                <Tooltip key={entry.key} title={isBasic ? BASIC_PERMISSION_HINT : SUPER_USER_ONLY_HINT}>
                  <span>{control}</span>
                </Tooltip>
              ) : (
                control
              )
            })}
          </Box>
        </Box>
      ))}
    </Stack>
  )
}
