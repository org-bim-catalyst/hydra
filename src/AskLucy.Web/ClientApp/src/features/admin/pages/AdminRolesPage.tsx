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
import * as adminRolesApi from '../api/adminRolesApi'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import type { RoleSummary } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { RoleEditorDialog } from '../components/RoleEditorDialog'
import { DeleteRoleDialog } from '../components/DeleteRoleDialog'
import { RolePermissionsDialog } from '../components/RolePermissionsDialog'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'
import { SelectAllScopeDialog } from '../components/SelectAllScopeDialog'
import type { SelectionScopeChoice } from '../components/SelectAllScopeDialog'
import { runBatchedBulkAction } from '../bulkRunner'

const ADMINISTRATOR_CONTENT_ACCESS_QUERY_KEY = ['admin', 'roles', 'administrator-content-access']

/** Roles screen (specs/055-role-management User Story 1) — define, edit, and delete custom roles; built-in roles are listed read-only. */
export function AdminRolesPage() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(20)
  const [editorOpen, setEditorOpen] = useState(false)
  const [editingRole, setEditingRole] = useState<RoleSummary | undefined>(undefined)
  const [deletingRole, setDeletingRole] = useState<RoleSummary | null>(null)
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
      setToastMessage(err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.')
    },
  })

  // A non-Super-User can neither delete nor bulk-delete a role carrying View user content (FR-016j).
  const isLockedRole = (role: RoleSummary) => !isSuperUser && isSuperUserControlledRole(role)

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
      title="Roles"
      subtitle={`${data?.totalCount ?? 0} roles`}
      actions={
        <Button startIcon={<AddIcon />} variant="contained" onClick={openCreate}>
          Create role
        </Button>
      }
    >
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TextField
          label="Search by name"
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
                action={<Button onClick={() => contentAccess.refetch()}>Retry</Button>}
              >
                {contentAccess.error instanceof ApiError
                  ? (contentAccess.error.detail ?? contentAccess.error.message)
                  : 'Could not load whether Administrators may view user content.'}
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
                label="Administrators may view user content"
              />
            )}
          </Box>
        )}

        {isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button onClick={() => refetch()}>Retry</Button>}
          >
            {error instanceof ApiError ? (error.detail ?? error.message) : 'Could not load roles.'}
          </Alert>
        )}

        {selection.selectedCount(allMatchingTotal) > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ mr: 1 }}>
              {selection.selectedCount(allMatchingTotal)} selected
            </Typography>
            <Button size="small" variant="outlined" color="error" onClick={beginBulkDelete}>
              Delete selected
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
                        input: { 'aria-label': 'Select all custom roles on this page' },
                      }}
                    />
                  </TableCell>
                  <TableCell>Name</TableCell>
                  <TableCell>Description</TableCell>
                  <TableCell>Permissions</TableCell>
                  <TableCell align="right">Users</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={6} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={6} message="No roles found." />
                )}
                {data?.items.map((role) => (
                  <TableRow key={role.id} hover>
                    <TableCell padding="checkbox">
                      {!role.isBuiltIn && !isLockedRole(role) && (
                        <Checkbox
                          checked={selection.isSelected(role.id)}
                          onChange={() => selection.toggleOne(role.id)}
                          slotProps={{ input: { 'aria-label': `Select ${role.name}` } }}
                        />
                      )}
                    </TableCell>
                    <TableCell>
                      {role.name}{' '}
                      {role.isBuiltIn && (
                        <Chip size="small" label="Built-in" variant="outlined" sx={{ ml: 0.5 }} />
                      )}
                    </TableCell>
                    <TableCell>{role.description}</TableCell>
                    <TableCell>
                      <Tooltip title={role.permissionKeys.join(', ')}>
                        <span>
                          {role.permissionKeys.length} permission
                          {role.permissionKeys.length === 1 ? '' : 's'}
                        </span>
                      </Tooltip>
                    </TableCell>
                    <TableCell align="right">{role.userCount}</TableCell>
                    <TableCell align="right">
                      <Tooltip title="View permissions">
                        <IconButton
                          size="small"
                          aria-label={`View permissions for ${role.name}`}
                          onClick={() => setViewingPermissionsRole(role)}
                        >
                          <VisibilityIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      {!role.isBuiltIn && (
                        <IconButton
                          size="small"
                          aria-label={`Actions for ${role.name}`}
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
          />
        </Paper>
      </Box>

      <Menu
        anchorEl={menuAnchor?.el}
        open={menuAnchor !== null}
        onClose={() => setMenuAnchor(null)}
      >
        <MenuItem onClick={() => menuAnchor && openEdit(menuAnchor.role)}>Edit&hellip;</MenuItem>
        <MenuItem
          disabled={menuAnchor !== null && isLockedRole(menuAnchor.role)}
          onClick={() => {
            if (menuAnchor) setDeletingRole(menuAnchor.role)
            setMenuAnchor(null)
          }}
        >
          {menuAnchor !== null && isLockedRole(menuAnchor.role) ? 'Delete (Super User only)' : <>Delete&hellip;</>}
        </MenuItem>
      </Menu>

      <RoleEditorDialog open={editorOpen} onClose={() => setEditorOpen(false)} role={editingRole} />
      {deletingRole && (
        <DeleteRoleDialog open onClose={() => setDeletingRole(null)} role={deletingRole} />
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
          actionLabel="Delete"
          progressVerb="Deleting"
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
