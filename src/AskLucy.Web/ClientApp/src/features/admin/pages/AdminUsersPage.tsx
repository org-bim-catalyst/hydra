import { useState } from 'react'
import {
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
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import * as adminApi from '../api/adminApi'
import type { UserSortBy } from '../api/adminApi'
import { AdminShell } from '../components/AdminShell'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { useMyProfile } from '../../profile/hooks/useProfile'
import { UserActionMenu } from '../components/UserActionMenu'

/**
 * Admin user management console (specs/001-admin-dashboard) — evolves the original
 * read-only grid (SPEC-000) with search/sort/pagination (FR-009/010/011) and row actions
 * (FR-012 through FR-016), deliberately never rendering passwordHash/securityStamp/
 * concurrencyStamp (FR-020), unlike the legacy page this replaces.
 */
export function AdminUsersPage() {
  const [search, setSearch] = useState('')
  const [sortBy, setSortBy] = useState<UserSortBy>('email')
  const [sortDescending, setSortDescending] = useState(false)
  const [page, setPage] = useState(0) // zero-based for MUI's TablePagination
  const [pageSize, setPageSize] = useState(20)

  const { data: profile } = useMyProfile()
  const isSuperUser = useIsSuperUser()

  const { data } = useQuery({
    queryKey: ['admin', 'users', { search, sortBy, sortDescending, page, pageSize }],
    queryFn: () => adminApi.getUsers({ search, sortBy, sortDescending, page: page + 1, pageSize }),
    placeholderData: (previous) => previous,
  })

  const toggleSort = (column: UserSortBy) => {
    if (sortBy === column) {
      setSortDescending((prev) => !prev)
    } else {
      setSortBy(column)
      setSortDescending(false)
    }
    setPage(0)
  }

  return (
    <AdminShell title="User management" subtitle={`${data?.totalCount ?? 0} registered users`}>
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
      <Paper elevation={1}>
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
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
              {data?.items.map((user) => (
                <TableRow key={user.id} hover>
                  <TableCell>{user.email}</TableCell>
                  <TableCell>{user.firstName}</TableCell>
                  <TableCell>{user.lastName}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      label={user.role}
                      color={user.role === 'Regular' ? 'default' : 'primary'}
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
    </AdminShell>
  )
}
