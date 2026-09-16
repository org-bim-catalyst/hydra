import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Typography,
} from '@mui/material'
import { ADMIN_PERMISSION_CATALOG } from '../adminPermissions'
import type { RoleSummary } from '../api/adminRolesApi'

interface RolePermissionsDialogProps {
  open: boolean
  onClose: () => void
  role: RoleSummary
}

/** Read-only view of every permission granted to a role, grouped by area. */
export function RolePermissionsDialog({ open, onClose, role }: RolePermissionsDialogProps) {
  const granted = new Set(role.permissionKeys)
  const entriesByArea = new Map<string, typeof ADMIN_PERMISSION_CATALOG>()

  for (const entry of ADMIN_PERMISSION_CATALOG) {
    if (!granted.has(entry.key)) continue
    const list = entriesByArea.get(entry.areaLabel) ?? []
    list.push(entry)
    entriesByArea.set(entry.areaLabel, list)
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Permissions for {role.name}</DialogTitle>
      <DialogContent>
        {entriesByArea.size === 0 ? (
          <Typography color="text.secondary">This role has no permissions granted.</Typography>
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
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} autoFocus>
          Close
        </Button>
      </DialogActions>
    </Dialog>
  )
}
