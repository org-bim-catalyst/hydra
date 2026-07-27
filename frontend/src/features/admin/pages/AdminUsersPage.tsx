import { Box, Paper, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '../../../api/httpClient'

interface UserAdminDto {
  id: string
  email: string
  firstName: string | null
  lastName: string | null
  emailConfirmed: boolean
  twoFactorEnabled: boolean
  lockoutEnabled: boolean
}

/**
 * Minimal replacement for the legacy Control Panel's user-management grid — note this
 * migration deliberately never renders passwordHash/securityStamp/concurrencyStamp
 * (FR-019), unlike the legacy page it replaces.
 */
export function AdminUsersPage() {
  const { data: users } = useQuery({
    queryKey: ['admin', 'users'],
    queryFn: () => apiFetch<UserAdminDto[]>('/users'),
  })

  return (
    <Box sx={{ p: 4 }}>
      <Paper sx={{ p: 2 }}>
        <Typography variant="h5" sx={{ mb: 2 }}>
          User Management
        </Typography>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Email</TableCell>
              <TableCell>First name</TableCell>
              <TableCell>Last name</TableCell>
              <TableCell>Email confirmed</TableCell>
              <TableCell>2FA enabled</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {users?.map((user) => (
              <TableRow key={user.id}>
                <TableCell>{user.email}</TableCell>
                <TableCell>{user.firstName}</TableCell>
                <TableCell>{user.lastName}</TableCell>
                <TableCell>{user.emailConfirmed ? 'Yes' : 'No'}</TableCell>
                <TableCell>{user.twoFactorEnabled ? 'Yes' : 'No'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Paper>
    </Box>
  )
}
