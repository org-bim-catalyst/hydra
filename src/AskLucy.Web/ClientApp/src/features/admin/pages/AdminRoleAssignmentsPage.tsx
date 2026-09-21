import { useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
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
  Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleAssignment } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { AssignRoleDialog } from '../components/AssignRoleDialog'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'
import { SelectAllScopeDialog } from '../components/SelectAllScopeDialog'
import type { SelectionScopeChoice } from '../components/SelectAllScopeDialog'
import { runBatchedBulkAction } from '../bulkRunner'

const NO_ROLE_FILTER = 'none'
const ANY_ROLE_FILTER = ''

/** Role assignments screen (specs/055-role-management User Story 2) — search users, filter by role, assign/change/remove. */
export function AdminRoleAssignmentsPage() {
  const [searchParams] = useSearchParams()
  // Deep-link from UserActionMenu's "Change role…" (specs/055-role-management FR-021).
  const [search, setSearch] = useState(searchParams.get('search') ?? '')
  const [roleFilter, setRoleFilter] = useState(ANY_ROLE_FILTER)
  const [page, setPage] = useState(0)
  const [pageSize, setPageSize] = useState(20)
  const [editingAssignment, setEditingAssignment] = useState<RoleAssignment | null>(null)

  const { data: roles } = useQuery({
    queryKey: ['admin', 'roles', 'all-for-picker'],
    queryFn: () => adminRolesApi.getRoles({ pageSize: 100 }),
  })

  const { data, error, refetch, isError, isLoading } = useQuery({
    queryKey: ['admin', 'role-assignments', { search, roleFilter, page, pageSize }],
    queryFn: () =>
      adminRolesApi.getRoleAssignments({
        search,
        roleId: roleFilter || undefined,
        page: page + 1,
        pageSize,
      }),
    placeholderData: (previous) => previous,
  })

  const queryClient = useQueryClient()
  const pickedRoleId = roleFilter && roleFilter !== NO_ROLE_FILTER ? roleFilter : null

  const selectableIds = (data?.items ?? [])
    .filter((a) => !a.isLockedOut && !a.role?.isBuiltIn)
    .map((a) => a.userId)
  const selection = useBulkSelection()

  const [scopeDialog, setScopeDialog] = useState<{ verb: 'Select' | 'Deselect' } | null>(null)

  // Eligibility here doesn't actually vary by role (only by search + the acting admin's own
  // Super User status — see RoleAssignmentRepository.ListEligibleIdsAsync), so this same query
  // can resolve the "all matching" total for selection purposes even before a role is picked.
  const { data: scopeTotalData } = useQuery({
    queryKey: ['admin', 'role-assignments', 'bulk-eligible-ids', search],
    queryFn: () =>
      adminRolesApi.getRoleAssignmentsEligibleIds(pickedRoleId ?? '', search || undefined),
    enabled: scopeDialog !== null || selection.isAllMatching,
  })
  const allMatchingTotal = scopeTotalData?.ids.length

  const [bulkAssignOpen, setBulkAssignOpen] = useState(false)
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

  async function beginBulkAssign() {
    if (pickedRoleId === null) return
    let targetIds: string[]
    if (selection.isAllMatching) {
      const eligible = await queryClient.fetchQuery({
        queryKey: ['admin', 'role-assignments', 'bulk-eligible-ids', search],
        queryFn: () =>
          adminRolesApi.getRoleAssignmentsEligibleIds(pickedRoleId, search || undefined),
      })
      targetIds = eligible.ids.filter((id) => !selection.excludedIds.has(id))
    } else {
      targetIds = [...selection.selectedIds]
    }
    setPendingTargetIds(targetIds)
    setBulkAssignOpen(true)
  }

  async function runBulkAssign(onProgress: (done: number, total: number) => void) {
    const ids = pendingTargetIds ?? []
    const outcome = await runBatchedBulkAction(
      ids,
      (batch) => adminRolesApi.bulkAssignRole(pickedRoleId!, { ids: batch, allMatching: false }),
      onProgress,
    )
    await queryClient.invalidateQueries({ queryKey: ['admin', 'role-assignments'] })
    selection.deselectAll()
    return outcome
  }

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (data?.items ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell title="Role assignments" subtitle={`${data?.totalCount ?? 0} users`}>
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <div style={{ display: 'flex', gap: 16, marginBottom: 16, flexWrap: 'wrap' }}>
          <TextField
            label="Search by name or email"
            size="small"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(0)
            }}
            sx={{ width: { xs: '100%', sm: 320 } }}
          />
          <TextField
            select
            label="Role"
            size="small"
            value={roleFilter}
            onChange={(e) => {
              setRoleFilter(e.target.value)
              setPage(0)
            }}
            sx={{ width: { xs: '100%', sm: 220 } }}
          >
            <MenuItem value={ANY_ROLE_FILTER}>Any role</MenuItem>
            <MenuItem value={NO_ROLE_FILTER}>No role</MenuItem>
            {roles?.items.map((role) => (
              <MenuItem key={role.id} value={role.id}>
                {role.name}
              </MenuItem>
            ))}
          </TextField>
        </div>

        {isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button onClick={() => refetch()}>Retry</Button>}
          >
            {error instanceof ApiError
              ? (error.detail ?? error.message)
              : 'Could not load role assignments.'}
          </Alert>
        )}

        {selection.selectedCount(allMatchingTotal) > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ mr: 1 }}>
              {selection.selectedCount(allMatchingTotal)} selected
            </Typography>
            <Button
              size="small"
              variant="outlined"
              disabled={pickedRoleId === null}
              onClick={beginBulkAssign}
            >
              Assign selected
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
                        input: { 'aria-label': 'Select all eligible users on this page' },
                      }}
                    />
                  </TableCell>
                  <TableCell>Email</TableCell>
                  <TableCell>Name</TableCell>
                  <TableCell>Role</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={6} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={6} message="No role assignments found." />
                )}
                {data?.items.map((assignment) => (
                  <TableRow key={assignment.userId} hover>
                    <TableCell padding="checkbox">
                      {!assignment.isLockedOut && !assignment.role?.isBuiltIn && (
                        <Checkbox
                          checked={selection.isSelected(assignment.userId)}
                          onChange={() => selection.toggleOne(assignment.userId)}
                          slotProps={{ input: { 'aria-label': `Select ${assignment.email}` } }}
                        />
                      )}
                    </TableCell>
                    <TableCell>{assignment.email}</TableCell>
                    <TableCell>
                      {[assignment.firstName, assignment.lastName].filter(Boolean).join(' ')}
                    </TableCell>
                    <TableCell>
                      {assignment.role ? (
                        <Chip
                          size="small"
                          label={assignment.role.name}
                          color="primary"
                          variant="outlined"
                        />
                      ) : (
                        <Chip size="small" label="No role" variant="outlined" />
                      )}
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={assignment.isLockedOut ? 'Locked' : 'Active'}
                        color={assignment.isLockedOut ? 'error' : 'success'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell align="right">
                      <Button
                        size="small"
                        disabled={assignment.isLockedOut}
                        onClick={() => setEditingAssignment(assignment)}
                      >
                        Change role&hellip;
                      </Button>
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

      {editingAssignment && (
        <AssignRoleDialog
          open
          onClose={() => setEditingAssignment(null)}
          assignment={editingAssignment}
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

      {bulkAssignOpen && pickedRoleId !== null && pendingTargetIds && (
        <BulkActionConfirmDialog
          open
          onClose={() => {
            setBulkAssignOpen(false)
            setPendingTargetIds(null)
          }}
          actionLabel="Assign"
          progressVerb="Assigning role to"
          itemCount={pendingTargetIds.length}
          onConfirm={runBulkAssign}
        />
      )}
    </AdminShell>
  )
}
