import { Box, Checkbox, FormControlLabel, Stack, Typography } from '@mui/material'
import { ADMIN_PERMISSION_CATALOG, type PermissionCatalogEntry } from '../adminPermissions'

function viewKeyFor(entry: PermissionCatalogEntry): string | null {
  if (entry.level !== 'Manage') return null
  return ADMIN_PERMISSION_CATALOG.find((p) => p.area === entry.area && p.level === 'View')?.key ?? null
}

interface PermissionPickerProps {
  selectedKeys: string[]
  onChange: (keys: string[]) => void
  disabled?: boolean
}

/** Grouped-by-area checkbox picker (FR-004): selecting Manage auto-selects its area's View. */
export function PermissionPicker({ selectedKeys, onChange, disabled }: PermissionPickerProps) {
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
      // Removing View also removes the area's Manage — a role can't manage what it can't view.
      if (entry.level === 'View') {
        const manageKey = ADMIN_PERMISSION_CATALOG.find((p) => p.area === entry.area && p.level === 'Manage')?.key
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
            {entries.map((entry) => (
              <FormControlLabel
                key={entry.key}
                control={
                  <Checkbox
                    size="small"
                    checked={selected.has(entry.key)}
                    onChange={() => toggle(entry)}
                    disabled={disabled}
                  />
                }
                label={entry.level}
                title={entry.description}
              />
            ))}
          </Box>
        </Box>
      ))}
    </Stack>
  )
}
