import {
  Box,
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
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
    <Box sx={{ p: { xs: 2, sm: 4 }, bgcolor: 'background.default', minHeight: '100%' }}>
      <Typography variant="h5" sx={{ mb: 0.5 }}>
        User management
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        {users?.length ?? 0} registered users
      </Typography>
      <Paper elevation={1}>
        <TableContainer>
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
                <TableRow key={user.id} hover>
                  <TableCell>{user.email}</TableCell>
                  <TableCell>{user.firstName}</TableCell>
                  <TableCell>{user.lastName}</TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      label={user.emailConfirmed ? 'Confirmed' : 'Pending'}
                      color={user.emailConfirmed ? 'success' : 'default'}
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
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
    </Box>
  )
}
