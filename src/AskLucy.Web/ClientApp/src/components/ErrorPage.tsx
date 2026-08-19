import { Box, Button, Stack, Typography } from '@mui/material'
import { isRouteErrorResponse, Link as RouterLink, useRouteError } from 'react-router'

export function ErrorPage() {
  const error = useRouteError()
  // No thrown error (e.g. the catch-all `*` route matched directly) is also a 404 —
  // only an explicit non-404 route error should show the generic failure message.
  const isNotFound = error === undefined || (isRouteErrorResponse(error) && error.status === 404)

  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100%', p: 4 }}>
      <Stack spacing={2} sx={{ alignItems: 'center', textAlign: 'center', maxWidth: 480 }}>
        <Typography variant="h3" sx={{ color: 'primary.main' }}>
          {isNotFound ? '404' : 'Something went wrong'}
        </Typography>
        <Typography variant="body1" color="text.secondary">
          {isNotFound
            ? "The page you're looking for doesn't exist or may have moved."
            : 'An unexpected error occurred. Please try again.'}
        </Typography>
        <Button component={RouterLink} to="/studio" variant="contained">
          Back to Ask Lucy
        </Button>
      </Stack>
    </Box>
  )
}
