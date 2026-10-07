import { useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Paper,
  Snackbar,
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
import { ApiError } from '../../../api/httpClient'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useFormat, useT } from '../../../i18n/useT'
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
  // The page body reads the language, so it must sit inside the surface; AdminShell's own surface wraps only its children.
  return (
    <LocalizedSurface scope="subtree">
      <AdminUsersPageContent />
    </LocalizedSurface>
  )
}

function AdminUsersPageContent() {
  const t = useT('admin.users')
  const format = useFormat()
  const [actionError, setActionError] = useState<string | null>(null)
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

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: ['admin', 'users', { search, sortBy, sortDescending, page, pageSize }],
    queryFn: () => adminApi.getUsers({ search, sortBy, sortDescending, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const selectableIds = (data?.items ?? []).filter((u) => u.id !== profile?.id).map((u) => u.id)
  const selection = useBulkSelection()

  const [scopeDialog, setScopeDialog] = useState<{ verb: 'Select' | 'Deselect' } | null>(null)

  const { data: scopeTotalData, isError: scopeTotalFailed } = useQuery({
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
    } catch (err) {
      // constitution VIII: a failed lookup of the eligible users must reach the admin.
      setActionError(err instanceof ApiError ? (err.detail ?? err.message) : t('page.generalError'))
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
    Lock: t('bulk.action.lock'),
    Unlock: t('bulk.action.unlock'),
    ForceReset2fa: t('bulk.action.forceReset2fa'),
    Delete: t('bulk.action.delete'),
  }
  const progressVerb: Record<UserBulkAction, string> = {
    Lock: t('bulk.progress.lock'),
    Unlock: t('bulk.progress.unlock'),
    ForceReset2fa: t('bulk.progress.forceReset2fa'),
    Delete: t('bulk.progress.delete'),
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
    <AdminShell
      title={t('page.title')}
      subtitle={t('page.subtitle', { count: data?.totalCount ?? 0 })}
    >
      <Box sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TextField
          label={t('page.searchLabel')}
          size="small"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value)
            setPage(0)
          }}
          sx={{ mb: 2, width: { xs: '100%', sm: 320 } }}
        />
        {(isError || scopeTotalFailed) && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button
                color="inherit"
                size="small"
                onClick={() => {
                  void refetch()
                }}
              >
                {t('page.retry')}
              </Button>
            }
          >
            {t('page.loadFailed')}
          </Alert>
        )}
        {selectedCount > 0 && (
          <Toolbar disableGutters sx={{ mb: 1, gap: 1 }}>
            <Typography variant="body2" sx={{ marginInlineEnd: '8px' }}>
              {t('selection.count', { count: selectedCount })}
            </Typography>
            <Button
              size="small"
              variant="outlined"
              disabled={resolvingAction}
              onClick={() => beginAction(lockUnlockAction)}
            >
              {lockUnlockAction === 'Unlock'
                ? t('selection.unlockSelected')
                : t('selection.lockSelected')}
            </Button>
            <Button
              size="small"
              variant="outlined"
              disabled={resolvingAction}
              onClick={() => beginAction('ForceReset2fa')}
            >
              {t('selection.force2faReset')}
            </Button>
            <Button
              size="small"
              variant="outlined"
              color="error"
              disabled={resolvingAction}
              onClick={() => beginAction('Delete')}
            >
              {t('selection.delete')}
            </Button>
          </Toolbar>
        )}
        <Paper
          elevation={1}
          sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
        >
          <TableContainer
            ref={tableRef}
            sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}
          >
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
                        input: { 'aria-label': t('table.selectAll') },
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
                      {t('table.columns.email')}
                    </TableSortLabel>
                  </TableCell>
                  <TableCell>{t('table.columns.firstName')}</TableCell>
                  <TableCell>{t('table.columns.lastName')}</TableCell>
                  <TableCell>{t('table.columns.role')}</TableCell>
                  <TableCell>{t('table.columns.status')}</TableCell>
                  <TableCell>{t('table.columns.emailConfirmed')}</TableCell>
                  <TableCell>{t('table.columns.twoFactor')}</TableCell>
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
                      {t('table.columns.registered')}
                    </TableSortLabel>
                  </TableCell>
                  <TableCell sx={{ textAlign: 'end' }}>{t('table.columns.actions')}</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {isLoading && <TableLoadingRow colSpan={10} />}
                {!isLoading && (data?.items ?? []).length === 0 && (
                  <TableEmptyRow colSpan={10} message={t('table.empty')} />
                )}
                {data?.items.map((user) => (
                  <TableRow key={user.id} hover>
                    <TableCell padding="checkbox">
                      {user.id !== profile?.id && (
                        <Checkbox
                          checked={selection.isSelected(user.id)}
                          onChange={() => selection.toggleOne(user.id)}
                          slotProps={{
                            input: { 'aria-label': t('table.selectUser', { email: user.email }) },
                          }}
                        />
                      )}
                    </TableCell>
                    <TableCell>
                      <bdi dir="ltr">{user.email}</bdi>
                    </TableCell>
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
                        label={
                          user.isLockedOut ? t('table.status.locked') : t('table.status.active')
                        }
                        color={user.isLockedOut ? 'error' : 'success'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={
                          user.emailConfirmed
                            ? t('table.confirmation.confirmed')
                            : t('table.confirmation.pending')
                        }
                        color={user.emailConfirmed ? 'success' : 'warning'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      <Chip
                        size="small"
                        label={
                          user.twoFactorEnabled
                            ? t('table.twoFactor.enabled')
                            : t('table.twoFactor.disabled')
                        }
                        color={user.twoFactorEnabled ? 'success' : 'default'}
                        variant="outlined"
                      />
                    </TableCell>
                    <TableCell>
                      {/* English keeps the browser's numeric short date it always showed. */}
                      {format.date(
                        user.createdAtUtc,
                        format.language === 'en' ? {} : { dateStyle: 'medium' },
                      )}
                    </TableCell>
                    <TableCell sx={{ textAlign: 'end' }}>
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
            labelRowsPerPage={t('pagination.rowsPerPage')}
            labelDisplayedRows={({ from, to, count }) =>
              t('pagination.displayedRows', { from, to, count: count === -1 ? to : count })
            }
            getItemAriaLabel={(type) => t(`pagination.${type}`)}
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

      <Snackbar
        open={actionError !== null}
        autoHideDuration={6000}
        onClose={() => setActionError(null)}
      >
        <Alert severity="error" variant="filled" onClose={() => setActionError(null)}>
          {actionError}
        </Alert>
      </Snackbar>
    </AdminShell>
  )
}
