import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  FormControlLabel,
  IconButton,
  Menu,
  MenuItem,
  Paper,
  Snackbar,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import AddIcon from '@mui/icons-material/Add'
import VisibilityIcon from '@mui/icons-material/Visibility'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { isSuperUserControlledRole } from '../adminPermissions'
import { ApiError } from '../../../api/httpClient'
import { useFormat, useT } from '../../../i18n/useT'
import * as adminRolesApi from '../api/adminRolesApi'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import type { RoleSummary } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { RoleEditorDialog } from '../components/RoleEditorDialog'
import { DeleteRoleDialog } from '../components/DeleteRoleDialog'
import { DuplicateRoleDialog } from '../components/DuplicateRoleDialog'
import { RolePermissionsDialog } from '../components/RolePermissionsDialog'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'
import { SelectAllScopeDialog } from '../components/SelectAllScopeDialog'
import type { SelectionScopeChoice } from '../components/SelectAllScopeDialog'
import { runBatchedBulkAction } from '../bulkRunner'

const ADMINISTRATOR_CONTENT_ACCESS_QUERY_KEY = ['admin', 'roles', 'administrator-content-access']

/**
 * Roles screen (specs/055-role-management User Story 1) — define, edit, and delete custom roles. Built-in
 * roles are read-only, except the User role, whose description and added permissions can be edited.
 * A Super User can duplicate any role into a new custom one.
 */
export function AdminRolesPage() {
  const t = useT('admin.roles')
  const tc = useT('common')
  const format = useFormat()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(20)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingRole, setEditingRole] = useState<RoleSummary | undefined>(undefined)
  const [deletingRole, setDeletingRole] = useState<RoleSummary | null>(null)
  const [duplicatingRole, setDuplicatingRole] = useState<RoleSummary | null>(null)
  const [viewingPermissionsRole, setViewingPermissionsRole] = useState<RoleSummary | null>(null)
  const [menuAnchor, setMenuAnchor] = useState<{ el: HTMLElement; role: RoleSummary } | null>(null)

  const queryClient = useQueryClient()
  const isSuperUser = useIsSuperUser()
  const [toastMessage, setToastMessage] = useState<string | null>(null)

  // specs/074 FR-016g: whether the built-in Administrator role holds View user content — a
  // Super User's switch. The server refuses anyone else; the switch isn't offered to them.
  const contentAccess = useQuery({
    queryKey: ADMINISTRATOR_CONTENT_ACCESS_QUERY_KEY,
    queryFn: adminRolesApi.getAdministratorContentAccess,
    enabled: isSuperUser,
  })
  const contentAccessMutation = useMutation({
    mutationFn: adminRolesApi.setAdministratorContentAccess,
    onSuccess: (result) => {
      queryClient.setQueryData(ADMINISTRATOR_CONTENT_ACCESS_QUERY_KEY, result)
      return queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] })
    },
    onError: (err: unknown) => {
      setToastMessage(err instanceof ApiError ? (err.detail ?? err.message) : tc('errors.generic'))
    },
  })

  // A non-Super-User can neither delete nor bulk-delete a role carrying View user content (FR-016j).
  const isLockedRole = (role: RoleSummary) => !isSuperUser && isSuperUserControlledRole(role)

  const canEdit = (role: RoleSummary) => !role.isBuiltIn || role.isDefault
  const hasActions = (role: RoleSummary) => canEdit(role) || isSuperUser

  const { data, error, refetch, isError, isLoading } = useQuery({
    queryKey: ['admin', 'roles', { search, page, pageSize }],
    queryFn: () => adminRolesApi.getRoles({ search, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const selectableIds = (data?.items ?? []).filter((r) => !r.isBuiltIn && !isLockedRole(r)).map((r) => r.id)
  const selection = useBulkSelection()

  const [scopeDialog, setScopeDialog] = useState<{ verb: 'Select' | 'Deselect' } | null>(null)

  const { data: scopeTotalData } = useQuery({
    queryKey: ['admin', 'roles', 'bulk-eligible-ids', search],
    queryFn: () => adminRolesApi.getRolesEligibleIds(search || undefined),
    enabled: scopeDialog !== null || selection.isAllMatching,
  })
  const allMatchingTotal = scopeTotalData?.ids.length

  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false)
  const [pendingTargetIds, setPendingTargetIds] = useState<string[] | null>(null)

  function handleHeaderCheckboxChange() {
    const state = selection.pageState(selectableIds)
    setScopeDialog({ verb: state === 'all' ? 'Deselect' : 'Select' })
  }

  function handleScopeChoice(scope: SelectionScopeChoice) {
    const verb = scopeDialog?.verb
    setScopeDialog(null)
    if (verb === 'Select') {
      if (scope === 'page') selection.selectPageOnly(selectableIds)
      else selection.selectAllMatching()
    } else if (verb === 'Deselect') {
      if (scope === 'page') selection.deselectPageOnly(selectableIds)
      else selection.deselectAll()
    }
  }

  async function beginBulkDelete() {
    try {
      let targetIds: string[]
      if (selection.isAllMatching) {
        const eligible = await queryClient.fetchQuery({
          queryKey: ['admin', 'roles', 'bulk-eligible-ids', search],
          queryFn: () => adminRolesApi.getRolesEligibleIds(search || undefined),
        })
        targetIds = eligible.ids.filter((id) => !selection.excludedIds.has(id))
      } else {
        targetIds = [...selection.selectedIds]
      }
      setPendingTargetIds(targetIds)
      setBulkDeleteOpen(true)
    } catch (err) {
      setToastMessage(err instanceof ApiError ? (err.detail ?? err.message) : t('errors.bulkPrepare'))
    }
  }

  async function runBulkDelete(onProgress: (done: number, total: number) => void) {
    const ids = pendingTargetIds ?? []
    const outcome = await runBatchedBulkAction(
      ids,
      (batch) => adminRolesApi.bulkDeleteRoles({ ids: batch, allMatching: false }),
      onProgress,
    )
    await queryClient.invalidateQueries({ queryKey: ['admin', 'roles'] })
    selection.deselectAll()
    return outcome
  }

  const openCreate = () => {
    setEditingRole(undefined)
    setEditorOpen(true)
  }

  const openEdit = (role: RoleSummary) => {
    setEditingRole(role)
    setEditorOpen(true)
    setMenuAnchor(null)
  }

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (data?.items ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title={t('title')}
      subtitle={t('subtitle', { count: data?.totalCount ?? 0 })}
      actions={
        <Button startIcon={<AddIcon />} variant="contained" onClick={openCreate}>
          {t('createRole')}
        </Button>
      }
    >
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TextField
          label={t('search')}
          size="small"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setPage(0)
          }}
          sx={{ mb: 2, width: { xs: '100%', sm: 320 } }}
        />

        {isSuperUser && (
          <Box sx={{ mb: 2 }}>
            {contentAccess.isError ? (
              <Alert
                severity="error"
                action={<Button onClick={() => contentAccess.refetch()}>{tc('actions.retry')}</Button>}
              >
                {contentAccess.error instanceof ApiError
                  ? (contentAccess.error.detail ?? contentAccess.error.message)
                  : t('errors.loadContentAccess')}
              </Alert>
            ) : (
              <FormControlLabel
                control={
                  <Switch
                    checked={contentAccess.data?.granted ?? false}
                    disabled={contentAccess.isLoading || contentAccessMutation.isPending}
                    onChange={(e) => contentAccessMutation.mutate(e.target.checked)}
                  />
                }
                label={t('contentAccess.label')}
              />
            )}
          </Box>
        )}

        {isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button onClick={() => refetch()}>{tc('actions.retry')}</Button>}
          >
            {error instanceof ApiError ? (error.detail ?? error.message) : t('errors.load')}
          </Alert>
        )}

        {selection.selectedCount(allMatchingTotal) > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ marginInlineEnd: '8px' }}>
              {t('selection.selectedCount', { count: selection.selectedCount(allMatchingTotal) })}
            </Typography>
            <Button size="small" variant="outlined" color="error" onClick={beginBulkDelete}>
              {t('selection.deleteSelected')}
            </Button>
          </Toolbar>
        )}
        <Paper
          elevation={1}
          sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
        >
          <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
            <Table sx={{ height: showsStatusRow ? '100%' : undefined }}>
              <TableHead>
                <TableRow>
                  <TableCell padding="checkbox">
                    <Checkbox
                      checked={selection.pageState(selectableIds) === 'all'}
                      indeterminate={selection.pageState(selectableIds) === 'partial'}
                      disabled={selectableIds.length === 0}
                      onChange={handleHeaderCheckboxChange}
                      slotProps={{
                        input: { 'aria-label': t('selection.selectAll') },
                      }}
                    />
                  </TableCell>
                  <TableCell>{t('table.name')}</TableCell>
                  <TableCell>{t('table.description')}</TableCell>
                  <TableCell>{t('table.permissions')}</TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>{t('table.users')}</TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>{t('table.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={6} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={6} message={t('table.empty')} />
                )}
                {data?.items.map((role) => (
                  <TableRow key={role.id} hover>
                    <TableCell padding="checkbox">
                      {!role.isBuiltIn && !isLockedRole(role) && (
                        <Checkbox
                          checked={selection.isSelected(role.id)}
                          onChange={() => selection.toggleOne(role.id)}
                          slotProps={{ input: { 'aria-label': t('selection.selectRole', { name: role.name }) } }}
                        />
                      )}
                    </TableCell>
                    <TableCell>
                      <bdi>{role.name}</bdi>{' '}
                      {role.isBuiltIn && (
                        <Chip size="small" label={t('table.builtIn')} variant="outlined" sx={{ marginInlineStart: '4px' }} />
                      )}
                      {role.isDefault && (
                        <Tooltip title={t('table.defaultHint')}>
                          <Chip size="small" label={t('table.default')} color="primary" variant="outlined" sx={{ marginInlineStart: '4px' }} />
                        </Tooltip>
                      )}
                    </TableCell>
                    <TableCell>{role.description}</TableCell>
                    <TableCell>
                      <Tooltip title={role.permissionKeys.join(', ')}>
                        <span>{t('table.permissionCount', { count: role.permissionKeys.length })}</span>
                      </Tooltip>
                    </TableCell>
                    <TableCell sx={{ textAlign: 'end' }}>
                      <bdi dir="ltr">{format.number(role.userCount)}</bdi>
                    </TableCell>
                    <TableCell sx={{ textAlign: 'end' }}>
                      <Tooltip title={t('table.viewPermissions')}>
                        <IconButton
                          size="small"
                          aria-label={t('table.viewPermissionsFor', { name: role.name })}
                          onClick={() => setViewingPermissionsRole(role)}
                        >
                          <VisibilityIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      {hasActions(role) && (
                        <IconButton
                          size="small"
                          aria-label={t('table.actionsFor', { name: role.name })}
                          onClick={(e) => setMenuAnchor({ el: e.currentTarget, role })}
                        >
                          <MoreVertIcon fontSize="small" />
                        </IconButton>
                      )}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
          <TablePagination
            component="div"
            count={data?.totalCount ?? 0}
            page={page}
            onPageChange={(_, newPage) => setPage(newPage)}
            rowsPerPage={pageSize}
            onRowsPerPageChange={(e) => {
              setPageSize(Number(e.target.value))
              setPage(0)
            }}
            rowsPerPageOptions={[10, 20, 50]}
            labelRowsPerPage={t('pagination.rowsPerPage')}
            labelDisplayedRows={({ from, to, count }) =>
              t('pagination.displayedRows', {
                from: format.number(from),
                to: format.number(to),
                count: format.number(count),
              })
            }
            getItemAriaLabel={(type) => t(`pagination.${type}`)}
          />
        </Paper>
      </Box>

      <Menu
        anchorEl={menuAnchor?.el}
        open={menuAnchor !== null}
        onClose={() => setMenuAnchor(null)}
      >
        {menuAnchor !== null && canEdit(menuAnchor.role) && (
          <MenuItem onClick={() => openEdit(menuAnchor.role)}>{t('menu.edit')}</MenuItem>
        )}
        {isSuperUser && (
          <MenuItem
            onClick={() => {
              if (menuAnchor) setDuplicatingRole(menuAnchor.role)
              setMenuAnchor(null)
            }}
          >
            {t('menu.duplicate')}
          </MenuItem>
        )}
        {menuAnchor !== null && !menuAnchor.role.isBuiltIn && (
          <MenuItem
            disabled={isLockedRole(menuAnchor.role)}
            onClick={() => {
              setDeletingRole(menuAnchor.role)
              setMenuAnchor(null)
            }}
          >
            {isLockedRole(menuAnchor.role) ? t('menu.deleteSuperUserOnly') : t('menu.delete')}
          </MenuItem>
        )}
      </Menu>

      <RoleEditorDialog open={editorOpen} onClose={() => setEditorOpen(false)} role={editingRole} />
      {deletingRole && (
        <DeleteRoleDialog open onClose={() => setDeletingRole(null)} role={deletingRole} />
      )}
      {duplicatingRole && (
        <DuplicateRoleDialog open onClose={() => setDuplicatingRole(null)} role={duplicatingRole} />
      )}
      {viewingPermissionsRole && (
        <RolePermissionsDialog
          open
          onClose={() => setViewingPermissionsRole(null)}
          role={viewingPermissionsRole}
        />
      )}

      {scopeDialog && (
        <SelectAllScopeDialog
          open
          onClose={() => setScopeDialog(null)}
          verb={scopeDialog.verb}
          pageCount={selectableIds.length}
          totalCount={allMatchingTotal}
          onChoose={handleScopeChoice}
        />
      )}

      {bulkDeleteOpen && pendingTargetIds && (
        <BulkActionConfirmDialog
          open
          onClose={() => {
            setBulkDeleteOpen(false)
            setPendingTargetIds(null)
          }}
          actionLabel={t('bulk.action')}
          progressVerb={t('bulk.progress')}
          itemCount={pendingTargetIds.length}
          onConfirm={runBulkDelete}
        />
      )}

      <Snackbar open={toastMessage !== null} autoHideDuration={6000} onClose={() => setToastMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setToastMessage(null)}>
          {toastMessage}
        </Alert>
      </Snackbar>
    </AdminShell>
  )
}
