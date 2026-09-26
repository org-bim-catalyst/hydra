import { Box, Checkbox, FormControlLabel, Stack, Tooltip, Typography } from '@mui/material'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { ADMIN_PERMISSION_CATALOG, SUPER_USER_CONTROLLED_KEYS, type PermissionCatalogEntry } from '../adminPermissions'

export const SUPER_USER_ONLY_HINT = 'Only a Super User can grant this'

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
}

/**
 * Grouped-by-area checkbox picker (FR-004): selecting Manage auto-selects its area's View.
 * Super-User-controlled keys (specs/074 FR-016f) stay visible to everyone but only a Super User
 * can tick or untick them; the server refuses the change regardless.
 */
export function PermissionPicker({ selectedKeys, onChange, disabled }: PermissionPickerProps) {
  const isSuperUser = useIsSuperUser()
  const selected = new Set(selectedKeys)

  const areas = [...new Set(ADMIN_PERMISSION_CATALOG.map((p) => p.area))].map((area) => ({
    area,
    label: ADMIN_PERMISSION_CATALOG.find((p) => p.area === area)!.areaLabel,
    entries: ADMIN_PERMISSION_CATALOG.filter((p) => p.area === area),
  }))

  const toggle = (entry: PermissionCatalogEntry) => {
    const next = new Set(selected)
    if (next.has(entry.key)) {
      next.delete(entry.key)
      // Removing the area's View also removes its Manage — a role can't manage what it can't view.
      if (entry.key === primaryKeyFor(entry.area, 'View')) {
        const manageKey = primaryKeyFor(entry.area, 'Manage')
        if (manageKey) next.delete(manageKey)
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
              const locked = !isSuperUser && SUPER_USER_CONTROLLED_KEYS.has(entry.key)
              const control = (
                <FormControlLabel
                  key={entry.key}
                  control={
                    <Checkbox
                      size="small"
                      checked={selected.has(entry.key)}
                      onChange={() => toggle(entry)}
                      disabled={disabled || locked}
                    />
                  }
                  label={entry.key === primaryKeyFor(entry.area, entry.level) ? entry.level : entry.displayName}
                  title={locked ? undefined : entry.description}
                />
              )

              return locked ? (
                <Tooltip key={entry.key} title={SUPER_USER_ONLY_HINT}>
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
