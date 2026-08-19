import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'

interface EmptyStateProps {
  icon?: ReactNode
  title: string
  description?: string
  action?: ReactNode
}

/** Shared "nothing here yet" surface for any list/panel that can be empty (FR-008),
 * consolidating what were previously ad hoc, feature-local empty messages. */
export function EmptyState({ icon, title, description, action }: EmptyStateProps) {
  return (
    <Stack
      spacing={1.5}
      sx={{
        alignItems: 'center',
        textAlign: 'center',
        py: 6,
        px: 3,
        color: 'text.secondary',
      }}
    >
      {icon && (
        <Box sx={{ color: 'text.disabled', fontSize: 40, display: 'flex' }} aria-hidden="true">
          {icon}
        </Box>
      )}
      {/* An empty-state placeholder message, not a document-outline heading — used inside many
          different heading contexts across the app, so it must not assert a fixed heading level
          (found via WorkflowDesignerPage.a11y.test.tsx: heading-order violation). */}
      <Typography variant="h6" component="div" color="text.primary">
        {title}
      </Typography>
      {description && <Typography variant="body2">{description}</Typography>}
      {action && <Box sx={{ pt: 1 }}>{action}</Box>}
    </Stack>
  )
}
