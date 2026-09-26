import { useState } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import { ADMIN_PERMISSION_CATALOG } from '../adminPermissions'
import type { RoleSummary } from '../api/adminRolesApi'

interface RolePermissionsDialogProps {
  open: boolean
  onClose: () => void
  role: RoleSummary
}

/**
 * Read-only view of every permission granted to a role, grouped by area. The search matches an
 * area, a permission's name or its description, so "users" finds the whole Users area.
 */
export function RolePermissionsDialog({ open, onClose, role }: RolePermissionsDialogProps) {
  const [search, setSearch] = useState('')
  const query = search.trim().toLowerCase()
  const granted = new Set(role.permissionKeys)
  const grantedEntries = ADMIN_PERMISSION_CATALOG.filter((entry) => granted.has(entry.key))
  const entriesByArea = new Map<string, typeof ADMIN_PERMISSION_CATALOG>()

  for (const entry of grantedEntries) {
    const matches =
      query.length === 0 ||
      [entry.areaLabel, entry.displayName, entry.description].some((text) => text.toLowerCase().includes(query))
    if (!matches) continue
    const list = entriesByArea.get(entry.areaLabel) ?? []
    list.push(entry)
    entriesByArea.set(entry.areaLabel, list)
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Permissions for {role.name}</DialogTitle>
      <DialogContent>
        {grantedEntries.length === 0 ? (
          <Typography color="text.secondary">This role has no permissions granted.</Typography>
        ) : (
          <>
            <TextField
              label="Search permissions"
              size="small"
              fullWidth
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              sx={{ mt: 1, mb: 1 }}
            />
            {entriesByArea.size === 0 ? (
              <Typography color="text.secondary">No permissions match “{search.trim()}”.</Typography>
            ) : (
              [...entriesByArea.entries()].map(([areaLabel, entries]) => (
                <div key={areaLabel}>
                  <Typography variant="subtitle2" sx={{ mt: 1 }}>
                    {areaLabel}
                  </Typography>
                  <List dense disablePadding>
                    {entries.map((entry) => (
                      <ListItem key={entry.key} disableGutters>
                        <ListItemText primary={entry.displayName} secondary={entry.description} />
                      </ListItem>
                    ))}
                  </List>
                </div>
              ))
            )}
          </>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} autoFocus>
          Close
        </Button>
      </DialogActions>
    </Dialog>
  )
}
