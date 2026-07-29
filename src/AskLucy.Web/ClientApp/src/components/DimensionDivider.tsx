import { Box, Typography } from '@mui/material'
import type { ReactNode } from 'react'

interface DimensionDividerProps {
  children: ReactNode
}

/**
 * A divider styled after a dimension line on a technical drawing — a ruled
 * line with a perpendicular tick at each end — rather than a plain MUI
 * <Divider>. Used sparingly: this is the one place that motif shows up
 * outside AuthLayout's side panel.
 */
export function DimensionDivider({ children }: DimensionDividerProps) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, my: 0.5 }}>
      <Box sx={{ flex: 1, height: 1, bgcolor: 'divider', position: 'relative' }}>
        <Box sx={{ position: 'absolute', left: 0, top: -3, width: '1px', height: 7, bgcolor: 'divider' }} />
      </Box>
      <Typography variant="overline" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>
        {children}
      </Typography>
      <Box sx={{ flex: 1, height: 1, bgcolor: 'divider', position: 'relative' }}>
        <Box sx={{ position: 'absolute', right: 0, top: -3, width: '1px', height: 7, bgcolor: 'divider' }} />
      </Box>
    </Box>
  )
}
