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
  Snackbar,
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
import { useFormat, useT } from '../../../i18n/useT'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { isSuperUserControlledRole } from '../adminPermissions'
import * as adminRolesApi from '../api/adminRolesApi'
import type { RoleAssignment } from '../api/adminRolesApi'
import { AdminShell } from '../components/AdminShell'
import { AssignRoleDialog } from '../components/AssignRoleDialog'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'
import { SelectAllScopeDialog } from '../components/SelectAllScopeDialog'
import type { SelectionScopeChoice } from '../components/SelectAllScopeDialog'
import { runBatchedBulkAction } from '../bulkRunner'

const ANY_ROLE_FILTER = ''

/** Role assignments screen (specs/055-role-management User Story 2) — search users, filter by role, assign/change/remove. */
export function AdminRoleAssignmentsPage() {
  const t = useT('admin.roleAssignments')
  const tc = useT('common')
  const format = useFormat()
  const [searchParams] = useSearchParams()
  const [toastMessage, setToastMessage] = useState<string | null>(null)
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

  // specs/074 FR-016f: only a Super User may give or take away a role carrying View user content.
  // UX only — the server refuses it with a 403 regardless.
  const isSuperUser = useIsSuperUser()
  const isLockedRole = (roleId: string | undefined) => {
    const role = roles?.items.find((r) => r.id === roleId)
    return !isSuperUser && role !== undefined && isSuperUserControlledRole(role)
  }
  // The User role is built-in but unprivileged, so its holders are as assignable as a custom role's.
  const isSelectable = (a: RoleAssignment) =>
    !a.isLockedOut && (!a.role?.isBuiltIn || adminRolesApi.isDefaultRole(a.role)) && !isLockedRole(a.role?.id)

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
  const pickedRoleId = roleFilter || null

  const selectableIds = (data?.items ?? []).filter(isSelectable).map((a) => a.userId)
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
    try {
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
    } catch (err) {
      setToastMessage(err instanceof ApiError ? (err.detail ?? err.message) : t('errors.bulkPrepare'))
    }
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
    <AdminShell
      title={t('title')}
      subtitle={t('subtitle', { count: data?.totalCount ?? 0 })}
    >
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <div style={{ display: 'flex', gap: 16, marginBottom: 16, flexWrap: 'wrap' }}>
          <TextField
            label={t('search')}
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
            label={t('roleFilter')}
            size="small"
            value={roleFilter}
            onChange={(e) => {
              setRoleFilter(e.target.value)
              setPage(0)
            }}
            sx={{ width: { xs: '100%', sm: 220 } }}
          >
            <MenuItem value={ANY_ROLE_FILTER}>{t('anyRole')}</MenuItem>
            {roles?.items.map((role) => (
              <MenuItem key={role.id} value={role.id}>
                {t(isLockedRole(role.id) ? 'roleOption.superUserOnly' : 'roleOption.plain', {
                  name: role.name,
                })}
              </MenuItem>
            ))}
          </TextField>
        </div>

        {isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button onClick={() => refetch()}>{tc('actions.retry')}</Button>}
          >
            {error instanceof ApiError
              ? (error.detail ?? error.message)
              : t('errors.load')}
          </Alert>
        )}

        {selection.selectedCount(allMatchingTotal) > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ marginInlineEnd: '8px' }}>
              {t('selection.selectedCount', { count: selection.selectedCount(allMatchingTotal) })}
            </Typography>
            <Button
              size="small"
              variant="outlined"
              disabled={pickedRoleId === null || isLockedRole(pickedRoleId)}
              onClick={beginBulkAssign}
            >
              {t('selection.assignSelected')}
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
                  <TableCell>{t('table.email')}</TableCell>
                  <TableCell>{t('table.name')}</TableCell>
                  <TableCell>{t('table.role')}</TableCell>
                  <TableCell>{t('table.status')}</TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>{t('table.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={6} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={6} message={t('table.empty')} />
                )}
                {data?.items.map((assignment) => (
                  <TableRow key={assignment.userId} hover>
                    <TableCell padding="checkbox">
                      {isSelectable(assignment) && (
                        <Checkbox
                          checked={selection.isSelected(assignment.userId)}
                          onChange={() => selection.toggleOne(assignment.userId)}
                          slotProps={{ input: { 'aria-label': t('selection.selectUser', { email: assignment.email }) } }}
                        />
                      )}
                    </TableCell>
                    <TableCell>
                      <bdi dir="ltr">{assignment.email}</bdi>
                    </TableCell>
                    <TableCell>
                      {[assignment.firstName, assignment.lastName].filter(Boolean).join(' ')}
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={assignment.role?.name ?? t('table.defaultRole')}
                        color={assignment.role && !adminRolesApi.isDefaultRole(assignment.role) ? 'primary' : 'default'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={assignment.isLockedOut ? t('table.locked') : t('table.active')}
                        color={assignment.isLockedOut ? 'error' : 'success'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell sx={{ textAlign: 'end' }}>
                      <Button
                        size="small"
                        disabled={assignment.isLockedOut}
                        onClick={() => setEditingAssignment(assignment)}
                      >
                        {t('table.changeRole')}
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
          actionLabel={t('bulk.action')}
          progressVerb={t('bulk.progress')}
          itemCount={pendingTargetIds.length}
          onConfirm={runBulkAssign}
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
