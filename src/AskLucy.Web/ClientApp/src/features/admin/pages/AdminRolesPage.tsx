import { useState } from 'react'
import {
  Alert,
  Button,
  Checkbox,
  Chip,
  IconButton,
  Menu,
  MenuItem,
  Paper,
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
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
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

  const { data, error, refetch, isError } = useQuery({
    queryKey: ['admin', 'roles', { search, page, pageSize }],
    queryFn: () => adminRolesApi.getRoles({ search, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const selectableIds = (data?.items ?? []).filter((r) => !r.isBuiltIn).map((r) => r.id)
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

      {isError && (
        <Alert severity="error" sx={{ mb: 2 }} action={<Button onClick={() => refetch()}>Retry</Button>}>
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
      <Paper elevation={1}>
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell padding="checkbox">
                  <Checkbox
                    checked={selection.pageState(selectableIds) === 'all'}
                    indeterminate={selection.pageState(selectableIds) === 'partial'}
                    disabled={selectableIds.length === 0}
                    onChange={handleHeaderCheckboxChange}
                    slotProps={{ input: { 'aria-label': 'Select all custom roles on this page' } }}
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
              {data?.items.map((role) => (
                <TableRow key={role.id} hover>
                  <TableCell padding="checkbox">
                    {!role.isBuiltIn && (
                      <Checkbox
                        checked={selection.isSelected(role.id)}
                        onChange={() => selection.toggleOne(role.id)}
                        slotProps={{ input: { 'aria-label': `Select ${role.name}` } }}
                      />
                    )}
                  </TableCell>
                  <TableCell>
                    {role.name}{' '}
                    {role.isBuiltIn && <Chip size="small" label="Built-in" variant="outlined" sx={{ ml: 0.5 }} />}
                  </TableCell>
                  <TableCell>{role.description}</TableCell>
                  <TableCell>
                    <Tooltip title={role.permissionKeys.join(', ')}>
                      <span>{role.permissionKeys.length} permission{role.permissionKeys.length === 1 ? '' : 's'}</span>
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

      <Menu anchorEl={menuAnchor?.el} open={menuAnchor !== null} onClose={() => setMenuAnchor(null)}>
        <MenuItem onClick={() => menuAnchor && openEdit(menuAnchor.role)}>Edit&hellip;</MenuItem>
        <MenuItem
          onClick={() => {
            if (menuAnchor) setDeletingRole(menuAnchor.role)
            setMenuAnchor(null)
          }}
        >
          Delete&hellip;
        </MenuItem>
      </Menu>

      <RoleEditorDialog open={editorOpen} onClose={() => setEditorOpen(false)} role={editingRole} />
      {deletingRole && (
        <DeleteRoleDialog open onClose={() => setDeletingRole(null)} role={deletingRole} />
      )}
      {viewingPermissionsRole && (
        <RolePermissionsDialog open onClose={() => setViewingPermissionsRole(null)} role={viewingPermissionsRole} />
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
    </AdminShell>
  )
}
