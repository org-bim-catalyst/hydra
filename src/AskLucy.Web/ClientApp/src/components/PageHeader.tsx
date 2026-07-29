import ArrowBackIcon from '@mui/icons-material/ArrowBack'
import { Box, IconButton, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink } from 'react-router'

interface PageHeaderProps {
  backTo: string
  backLabel: string
  title: string
  subtitle?: string
  actions?: ReactNode
}

/** Consistent back-navigation + title for every page reached from UserMenu (Settings,
 * Profile, Admin Dashboard) or nested one level deeper (Admin Users, back to the
 * dashboard) — previously the only way back to Chat was the user menu or browser back. */
export function PageHeader({ backTo, backLabel, title, subtitle, actions }: PageHeaderProps) {
  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-start', justifyContent: 'space-between', mb: 3 }}>
      <Stack direction="row" spacing={1}>
        <IconButton component={RouterLink} to={backTo} aria-label={backLabel} sx={{ mt: -0.5, ml: -1 }}>
          <ArrowBackIcon />
        </IconButton>
        <Box>
          <Typography variant="h5">{title}</Typography>
          {subtitle && (
            <Typography variant="body2" color="text.secondary">
              {subtitle}
            </Typography>
          )}
        </Box>
      </Stack>
      {actions}
    </Stack>
  )
}
