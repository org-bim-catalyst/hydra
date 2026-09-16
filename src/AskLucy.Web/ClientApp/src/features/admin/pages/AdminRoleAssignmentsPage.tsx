import { useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Alert,
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
import { ApiError } from '../../../api/httpClient'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleAssignment } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { AssignRoleDialog } from '../components/AssignRoleDialog'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'

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

  const { data, error, refetch, isError } = useQuery({
    queryKey: ['admin', 'role-assignments', { search, roleFilter, page, pageSize }],
    queryFn: () => adminRolesApi.getRoleAssignments({ search, roleId: roleFilter || undefined, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const queryClient = useQueryClient()
  const pickedRoleId = roleFilter && roleFilter !== NO_ROLE_FILTER ? roleFilter : null

  const selectableIds = (data?.items ?? [])
    .filter((a) => !a.isLockedOut && !a.role?.isBuiltIn)
    .map((a) => a.userId)
  const selection = useBulkSelection(selectableIds)

  const [bulkAssignOpen, setBulkAssignOpen] = useState(false)

  const { data: eligibleIdsData } = useQuery({
    queryKey: ['admin', 'role-assignments', 'bulk-eligible-ids', pickedRoleId, search],
    queryFn: () => adminRolesApi.getRoleAssignmentsEligibleIds(pickedRoleId!, search || undefined),
    enabled: bulkAssignOpen && pickedRoleId !== null,
  })

  async function runBulkAssign(scope: 'page' | 'all') {
    const target: adminRolesApi.BulkTargetRequest =
      scope === 'all'
        ? { ids: null, allMatching: true, search: search || undefined }
        : { ids: [...selection.selected], allMatching: false }

    const result = await adminRolesApi.bulkAssignRole(pickedRoleId!, target)
    await queryClient.invalidateQueries({ queryKey: ['admin', 'role-assignments'] })
    selection.clear()
    return result
  }

  return (
    <AdminShell title="Role assignments" subtitle={`${data?.totalCount ?? 0} users`}>
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
        <Alert severity="error" sx={{ mb: 2 }} action={<Button onClick={() => refetch()}>Retry</Button>}>
          {error instanceof ApiError ? (error.detail ?? error.message) : 'Could not load role assignments.'}
        </Alert>
      )}

      {selection.selectedCount > 0 && (
        <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
          <Typography variant="body2" sx={{ mr: 1 }}>
            {selection.selectedCount} selected
          </Typography>
          <Button
            size="small"
            variant="outlined"
            disabled={pickedRoleId === null}
            onClick={() => setBulkAssignOpen(true)}
          >
            Assign selected
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
                    checked={selection.isAllSelected}
                    indeterminate={selection.isIndeterminate}
                    disabled={selectableIds.length === 0}
                    onChange={selection.toggleAll}
                    slotProps={{ input: { 'aria-label': 'Select all eligible users on this page' } }}
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
              {data?.items.map((assignment) => (
                <TableRow key={assignment.userId} hover>
                  <TableCell padding="checkbox">
                    {!assignment.isLockedOut && !assignment.role?.isBuiltIn && (
                      <Checkbox
                        checked={selection.selected.has(assignment.userId)}
                        onChange={() => selection.toggleOne(assignment.userId)}
                        slotProps={{ input: { 'aria-label': `Select ${assignment.email}` } }}
                      />
                    )}
                  </TableCell>
                  <TableCell>{assignment.email}</TableCell>
                  <TableCell>{[assignment.firstName, assignment.lastName].filter(Boolean).join(' ')}</TableCell>
                  <TableCell>
                    {assignment.role ? (
                      <Chip size="small" label={assignment.role.name} color="primary" variant="outlined" />
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

      {editingAssignment && (
        <AssignRoleDialog open onClose={() => setEditingAssignment(null)} assignment={editingAssignment} />
      )}

      {bulkAssignOpen && pickedRoleId !== null && (
        <BulkActionConfirmDialog
          open
          onClose={() => setBulkAssignOpen(false)}
          actionLabel="Assign"
          pageSelectedCount={selection.selectedCount}
          allMatchingCount={eligibleIdsData?.ids.length}
          onConfirm={runBulkAssign}
        />
      )}
    </AdminShell>
  )
}
