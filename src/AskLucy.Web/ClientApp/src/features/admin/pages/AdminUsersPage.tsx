import { useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Box,
  Button,
  Checkbox,
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TablePagination,
  TableRow,
  TableSortLabel,
  TextField,
  Toolbar,
  Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import * as adminApi from '../api/adminApi'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import type { UserBulkAction, UserSortBy } from '../api/adminApi'
import { AdminShell } from '../components/AdminShell'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { ADMIN_ROLES } from '../../../hooks/useIsAdmin'
import { useMyProfile } from '../../profile/hooks/useProfile'
import { UserActionMenu } from '../components/UserActionMenu'
import { useBulkSelection } from '../hooks/useBulkSelection'
import { BulkActionConfirmDialog } from '../components/BulkActionConfirmDialog'
import { SelectAllScopeDialog } from '../components/SelectAllScopeDialog'
import type { SelectionScopeChoice } from '../components/SelectAllScopeDialog'
import { runBatchedBulkAction } from '../bulkRunner'

/** Every action's eligibility excludes only self (never already-locked/unlocked), so any one of
 * them is a valid stand-in for "everyone matching this filter" when resolving selection scope
 * (as opposed to a specific action's own narrower eligibility, resolved separately at run time). */
const SELECTION_SCOPE_ACTION: UserBulkAction = 'ForceReset2fa'

/**
 * Admin user management console (specs/001-admin-dashboard) — evolves the original
 * read-only grid (SPEC-000) with search/sort/pagination (FR-009/010/011) and row actions
 * (FR-012 through FR-016), deliberately never rendering passwordHash/securityStamp/
 * concurrencyStamp (FR-020), unlike the legacy page this replaces.
 */
export function AdminUsersPage() {
  // `?search=` lets another admin page link straight to a user (specs/074 FR-017); read once.
  const [searchParams] = useSearchParams()
  const [search, setSearch] = useState(() => searchParams.get('search') ?? '')
  const [sortBy, setSortBy] = useState<UserSortBy>('email')
  const [sortDescending, setSortDescending] = useState(false)
  const [page, setPage] = useState(0) // zero-based for MUI's TablePagination
  const [pageSize, setPageSize] = useState(20)

  const { data: profile } = useMyProfile()
  const isSuperUser = useIsSuperUser()
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'users', { search, sortBy, sortDescending, page, pageSize }],
    queryFn: () => adminApi.getUsers({ search, sortBy, sortDescending, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const selectableIds = (data?.items ?? []).filter((u) => u.id !== profile?.id).map((u) => u.id)
  const selection = useBulkSelection()

  const [scopeDialog, setScopeDialog] = useState<{ verb: 'Select' | 'Deselect' } | null>(null)

  const { data: scopeTotalData } = useQuery({
    queryKey: ['admin', 'users', 'bulk-eligible-ids', SELECTION_SCOPE_ACTION, search],
    queryFn: () => adminApi.getUsersEligibleIds(SELECTION_SCOPE_ACTION, search || undefined),
    enabled: scopeDialog !== null || selection.isAllMatching,
  })
  const allMatchingTotal = scopeTotalData?.ids.length

  const selectedUsers = (data?.items ?? []).filter((u) => selection.isSelected(u.id))
  const allSelectedAreLockedOut =
    selectedUsers.length > 0 && selectedUsers.every((u) => u.isLockedOut)
  const lockUnlockAction: UserBulkAction = allSelectedAreLockedOut ? 'Unlock' : 'Lock'

  const [pendingAction, setPendingAction] = useState<UserBulkAction | null>(null)
  const [pendingTargetIds, setPendingTargetIds] = useState<string[] | null>(null)
  const [resolvingAction, setResolvingAction] = useState(false)

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

  async function beginAction(action: UserBulkAction) {
    setResolvingAction(true)
    try {
      let targetIds: string[]
      if (selection.isAllMatching) {
        const eligible = await queryClient.fetchQuery({
          queryKey: ['admin', 'users', 'bulk-eligible-ids', action, search],
          queryFn: () => adminApi.getUsersEligibleIds(action, search || undefined),
        })
        targetIds = eligible.ids.filter((id) => !selection.excludedIds.has(id))
      } else {
        targetIds = [...selection.selectedIds]
      }
      setPendingTargetIds(targetIds)
      setPendingAction(action)
    } finally {
      setResolvingAction(false)
    }
  }

  async function runBulkAction(onProgress: (done: number, total: number) => void) {
    const ids = pendingTargetIds ?? []
    const run =
      pendingAction === 'Lock'
        ? adminApi.bulkLockUsers
        : pendingAction === 'Unlock'
          ? adminApi.bulkUnlockUsers
          : pendingAction === 'ForceReset2fa'
            ? adminApi.bulkForceReset2fa
            : adminApi.bulkDeleteUsers

    const outcome = await runBatchedBulkAction(
      ids,
      (batch) => run({ ids: batch, allMatching: false }),
      onProgress,
    )

    await queryClient.invalidateQueries({ queryKey: ['admin', 'users'] })
    selection.deselectAll()
    return outcome
  }

  const actionLabel: Record<UserBulkAction, string> = {
    Lock: 'Lock',
    Unlock: 'Unlock',
    ForceReset2fa: 'Force 2FA reset',
    Delete: 'Delete',
  }
  const progressVerb: Record<UserBulkAction, string> = {
    Lock: 'Locking',
    Unlock: 'Unlocking',
    ForceReset2fa: 'Resetting 2FA for',
    Delete: 'Deleting',
  }

  const toggleSort = (column: UserSortBy) => {
    if (sortBy === column) {
      setSortDescending((prev) => !prev)
    } else {
      setSortBy(column)
      setSortDescending(false)
    }
    setPage(0)
  }

  const currentPageState = selection.pageState(selectableIds)
  const selectedCount = selection.selectedCount(allMatchingTotal)

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (data?.items ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell title="User management" subtitle={`${data?.totalCount ?? 0} registered users`}>
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TextField
          label="Search by name or email"
          size="small"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setPage(0)
          }}
          sx={{ mb: 2, width: { xs: '100%', sm: 320 } }}
        />
        {selectedCount > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ mr: 1 }}>
              {selectedCount} selected
            </Typography>
            <Button
              size="small"
              variant="outlined"
              disabled={resolvingAction}
              onClick={() => beginAction(lockUnlockAction)}
            >
              {lockUnlockAction === 'Unlock' ? 'Unlock selected' : 'Lock selected'}
            </Button>
            <Button
              size="small"
              variant="outlined"
              disabled={resolvingAction}
              onClick={() => beginAction('ForceReset2fa')}
            >
              Force 2FA reset
            </Button>
            <Button
              size="small"
              variant="outlined"
              color="error"
              disabled={resolvingAction}
              onClick={() => beginAction('Delete')}
            >
              Delete
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
                      checked={currentPageState === 'all'}
                      indeterminate={currentPageState === 'partial'}
                      disabled={selectableIds.length === 0}
                      onChange={handleHeaderCheckboxChange}
                      slotProps={{
                        input: { 'aria-label': 'Select all eligible users on this page' },
                      }}
                    />
                  </TableCell>
                  <TableCell
                    sortDirection={sortBy === 'email' ? (sortDescending ? 'desc' : 'asc') : false}
                  >
                    <TableSortLabel
                      active={sortBy === 'email'}
                      direction={sortBy === 'email' && sortDescending ? 'desc' : 'asc'}
                      onClick={() => toggleSort('email')}
                    >
                      Email
                    </TableSortLabel>
                  </TableCell>
                  <TableCell>First name</TableCell>
                  <TableCell>Last name</TableCell>
                  <TableCell>Role</TableCell>
                  <TableCell>Status</TableCell>
                  <TableCell>Email confirmed</TableCell>
                  <TableCell>2FA enabled</TableCell>
                  <TableCell
                    sortDirection={
                      sortBy === 'createdAtUtc' ? (sortDescending ? 'desc' : 'asc') : false
                    }
                  >
                    <TableSortLabel
                      active={sortBy === 'createdAtUtc'}
                      direction={sortBy === 'createdAtUtc' && sortDescending ? 'desc' : 'asc'}
                      onClick={() => toggleSort('createdAtUtc')}
                    >
                      Registered
                    </TableSortLabel>
                  </TableCell>
                  <TableCell align="right">Actions</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={10} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={10} message="No users found." />
                )}
                {data?.items.map((user) => (
                  <TableRow key={user.id} hover>
                    <TableCell padding="checkbox">
                      {user.id !== profile?.id && (
                        <Checkbox
                          checked={selection.isSelected(user.id)}
                          onChange={() => selection.toggleOne(user.id)}
                          slotProps={{ input: { 'aria-label': `Select ${user.email}` } }}
                        />
                      )}
                    </TableCell>
                    <TableCell>{user.email}</TableCell>
                    <TableCell>{user.firstName}</TableCell>
                    <TableCell>{user.lastName}</TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={user.role}
                        color={ADMIN_ROLES.includes(user.role) ? 'primary' : 'default'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={user.isLockedOut ? 'Locked' : 'Active'}
                        color={user.isLockedOut ? 'error' : 'success'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={user.emailConfirmed ? 'Confirmed' : 'Pending'}
                        color={user.emailConfirmed ? 'success' : 'warning'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={user.twoFactorEnabled ? 'Enabled' : 'Disabled'}
                        color={user.twoFactorEnabled ? 'success' : 'default'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>{new Date(user.createdAtUtc).toLocaleDateString()}</TableCell>
                    <TableCell align="right">
                      <UserActionMenu
                        user={user}
                        isSelf={user.id === profile?.id}
                        isSuperUser={isSuperUser}
                      />
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

      {pendingAction && pendingTargetIds && (
        <BulkActionConfirmDialog
          open
          onClose={() => {
            setPendingAction(null)
            setPendingTargetIds(null)
          }}
          actionLabel={actionLabel[pendingAction]}
          progressVerb={progressVerb[pendingAction]}
          itemCount={pendingTargetIds.length}
          onConfirm={runBulkAction}
        />
      )}
    </AdminShell>
  )
}
