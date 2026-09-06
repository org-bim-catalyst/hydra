import { Box, CircularProgress } from '@mui/material'

/** Shown by the route guards while the session check is in flight. */
export function RouteLoading() {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100%', p: 4 }}>
      <CircularProgress />
    </Box>
  )
}
