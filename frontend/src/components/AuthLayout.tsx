import { Avatar, Box, Paper, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'

interface AuthLayoutProps {
  title: string
  children: ReactNode
}

export function AuthLayout({ title, children }: AuthLayoutProps) {
  return (
    <Box
      sx={{
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        minHeight: '100%',
        p: 2,
        background: (theme) =>
          theme.palette.mode === 'dark'
            ? `radial-gradient(circle at top, ${theme.palette.primary.dark}22, transparent 60%), ${theme.palette.background.default}`
            : `radial-gradient(circle at top, ${theme.palette.primary.light}22, transparent 60%), ${theme.palette.background.default}`,
      }}
    >
      <Paper elevation={2} sx={{ p: { xs: 3, sm: 5 }, maxWidth: 420, width: '100%' }}>
        <Stack spacing={1} sx={{ mb: 4, alignItems: 'center', textAlign: 'center' }}>
          <Avatar src="/lucy.png" alt="Ask Lucy" sx={{ width: 64, height: 64 }} />
          <Typography variant="h4" sx={{ color: 'primary.main' }}>
            Ask Lucy
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {title}
          </Typography>
        </Stack>
        {children}
      </Paper>
    </Box>
  )
}
