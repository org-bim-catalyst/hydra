import { Link, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import type { UserRef } from '../../api/adminOperationalFailuresApi'
import { usersSearchRoute } from './operationalFailureLabels'

/**
 * A user as the trail shows them (FR-016, FR-029a): a live account links to the Users page
 * filtered to them; a deleted one keeps its name without a link; an erased one has nothing left.
 */
export function UserRefLink({ user }: { user: UserRef }) {
  if (user.status === 'Erased') {
    return (
      <Typography component="span" variant="body2" color="text.secondary">
        erased user
      </Typography>
    )
  }

  const name = user.displayName ?? user.email ?? 'Unknown user'
  if (user.status === 'Deleted' || !user.email) {
    return (
      <Typography component="span" variant="body2" color="text.secondary">
        {name}
        {user.status === 'Deleted' ? ' (deleted)' : ''}
      </Typography>
    )
  }

  return (
    <Link component={RouterLink} to={usersSearchRoute(user.email)} variant="body2">
      {name}
    </Link>
  )
}
