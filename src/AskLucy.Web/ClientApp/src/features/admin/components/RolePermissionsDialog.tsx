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
import { useT } from '../../../i18n/useT'
import { ADMIN_PERMISSION_CATALOG } from '../adminPermissions'
import { usePermissionText } from '../hooks/usePermissionText'
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
  const t = useT('admin.roles')
  const text = usePermissionText()
  const [search, setSearch] = useState('')
  const query = search.trim().toLowerCase()
  const granted = new Set(role.permissionKeys)
  const grantedEntries = ADMIN_PERMISSION_CATALOG.filter((entry) => granted.has(entry.key))
  // Grouped by the permission area, labelled and searched in the language on screen.
  const entriesByArea = new Map<string, typeof ADMIN_PERMISSION_CATALOG>()

  for (const entry of grantedEntries) {
    const matches =
      query.length === 0 ||
      [text.area(entry), text.name(entry), text.description(entry)].some((value) =>
        value.toLowerCase().includes(query),
      )
    if (!matches) continue
    const list = entriesByArea.get(entry.area) ?? []
    list.push(entry)
    entriesByArea.set(entry.area, list)
  }

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('permissionsDialog.title', { name: role.name })}</DialogTitle>
      <DialogContent>
        {grantedEntries.length === 0 ? (
          <Typography color="text.secondary">{t('permissionsDialog.none')}</Typography>
        ) : (
          <>
            <TextField
              label={t('permissionsDialog.search')}
              size="small"
              fullWidth
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              sx={{ mt: 1, mb: 1 }}
            />
            {entriesByArea.size === 0 ? (
              <Typography color="text.secondary">{t('permissionsDialog.noMatch', { query: search.trim() })}</Typography>
            ) : (
              [...entriesByArea.entries()].map(([area, entries]) => (
                <div key={area}>
                  <Typography variant="subtitle2" sx={{ mt: 1 }}>
                    {text.area(entries[0])}
                  </Typography>
                  <List dense disablePadding>
                    {entries.map((entry) => (
                      <ListItem key={entry.key} disableGutters>
                        <ListItemText primary={text.name(entry)} secondary={text.description(entry)} />
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
          {t('permissionsDialog.close')}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
