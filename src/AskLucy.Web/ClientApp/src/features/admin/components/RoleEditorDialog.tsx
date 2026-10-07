import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Snackbar,
  TextField,
} from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import { useT } from '../../../i18n/useT'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleSummary } from '../api/adminRolesApi'
import { SUPER_USER_CONTROLLED_KEYS } from '../adminPermissions'
import { PermissionPicker } from './PermissionPicker'

interface RoleEditorDialogProps {
  open: boolean
  onClose: () => void
  /** Present when editing; absent when creating. */
  role?: RoleSummary
}

const ROLES_QUERY_KEY = ['admin', 'roles']

/**
 * Create/edit form for a custom role (US1) — mirrors CreateRoleCommandValidator/UpdateRoleCommandValidator's
 * rules for immediate inline feedback. For the built-in User role it edits only the description and
 * the permissions added on top of its basic ones: the name is fixed, the basics can't be unticked, and
 * View user content isn't offered (every account holds this role).
 */
export function RoleEditorDialog({ open, onClose, role }: RoleEditorDialogProps) {
  const queryClient = useQueryClient()
  const t = useT('admin.roles')
  const tc = useT('common')
  const isEdit = role !== undefined
  const isDefault = role?.isDefault === true

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [permissionKeys, setPermissionKeys] = useState<string[]>([])
  const [nameError, setNameError] = useState<string | null>(null)
  const [permissionError, setPermissionError] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  // Re-initializes the form fields whenever the dialog transitions to open, without an effect
  // (this component stays mounted across open/close, so an effect would run one render late —
  // see https://react.dev/learn/you-might-not-need-an-effect#adjusting-some-state-when-a-prop-changes).
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setName(role?.name ?? '')
      setDescription(role?.description ?? '')
      setPermissionKeys(role?.permissionKeys ?? [])
      setNameError(null)
      setPermissionError(null)
      setErrorMessage(null)
    }
  }

  const onError = (err: unknown) => {
    setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : tc('errors.generic'))
  }

  const createMutation = useMutation({
    mutationFn: () => adminRolesApi.createRole({ name: name.trim(), description: description.trim() || null, permissionKeys }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError,
  })

  const updateMutation = useMutation({
    mutationFn: () =>
      isDefault
        ? adminRolesApi.updateDefaultRole({
            description: description.trim() || null,
            permissionKeys: [...new Set([...role!.lockedPermissionKeys, ...permissionKeys])],
            concurrencyStamp: role!.concurrencyStamp,
          })
        : adminRolesApi.updateRole(role!.id, {
            name: name.trim(),
            description: description.trim() || null,
            permissionKeys,
            concurrencyStamp: role!.concurrencyStamp,
          }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ROLES_QUERY_KEY })
      onClose()
    },
    onError,
  })

  const isPending = createMutation.isPending || updateMutation.isPending

  const validate = (): boolean => {
    const trimmed = name.trim()
    let valid = true

    if (trimmed.length < 2 || trimmed.length > 50) {
      setNameError(t('editor.nameLength'))
      valid = false
    } else {
      setNameError(null)
    }

    // The User role may hold nothing beyond its basic permissions.
    if (permissionKeys.length === 0 && !isDefault) {
      setPermissionError(t('editor.permissionRequired'))
      valid = false
    } else {
      setPermissionError(null)
    }

    return valid
  }

  const handleSave = () => {
    if (!validate()) return
    if (isEdit) updateMutation.mutate()
    else createMutation.mutate()
  }

  return (
    <>
      <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
        <DialogTitle>{isEdit ? t('editor.editTitle', { name: role.name }) : t('editor.createTitle')}</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus={!isDefault}
            label={t('editor.name')}
            fullWidth
            value={name}
            disabled={isDefault}
            onChange={(e) => setName(e.target.value)}
            error={nameError !== null}
            helperText={nameError ?? (isDefault ? t('editor.defaultNameHint') : undefined)}
            sx={{ mt: 1, mb: 2 }}
          />
          <TextField
            label={t('editor.description')}
            fullWidth
            multiline
            minRows={2}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            slotProps={{ htmlInput: { maxLength: 250 } }}
          />
          <PermissionPicker
            selectedKeys={permissionKeys}
            onChange={setPermissionKeys}
            lockedKeys={isDefault ? role.lockedPermissionKeys : undefined}
            hiddenKeys={isDefault ? SUPER_USER_CONTROLLED_KEYS : undefined}
          />
          {permissionError && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {permissionError}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>{t('editor.cancel')}</Button>
          <Button onClick={handleSave} variant="contained" disabled={isPending}>
            {isEdit ? t('editor.save') : t('editor.create')}
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar open={errorMessage !== null} autoHideDuration={6000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </>
  )
}
