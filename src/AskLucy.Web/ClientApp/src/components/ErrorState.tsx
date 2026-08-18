import ErrorOutlineIcon from '@mui/icons-material/ErrorOutlineOutlined'
import { Box, Button, Stack, Typography } from '@mui/material'

interface ErrorStateProps {
  title: string
  description?: string
  onRetry?: () => void
}

/** Shared in-panel/in-list failure state (FR-008) — distinct from the full-page
 * `ErrorPage` router `errorElement`, which handles route-level crashes, not "this one
 * list/panel failed to load." */
export function ErrorState({ title, description, onRetry }: ErrorStateProps) {
  return (
    <Stack
      role="alert"
      spacing={1.5}
      sx={{
        alignItems: 'center',
        textAlign: 'center',
        py: 6,
        px: 3,
        color: 'text.secondary',
      }}
    >
      <Box sx={{ color: 'error.main', fontSize: 40, display: 'flex' }} aria-hidden="true">
        <ErrorOutlineIcon fontSize="inherit" />
      </Box>
      <Typography variant="h6" color="text.primary">
        {title}
      </Typography>
      {description && <Typography variant="body2">{description}</Typography>}
      {onRetry && (
        <Button variant="outlined" size="small" onClick={onRetry} sx={{ mt: 1 }}>
          Retry
        </Button>
      )}
    </Stack>
  )
}
