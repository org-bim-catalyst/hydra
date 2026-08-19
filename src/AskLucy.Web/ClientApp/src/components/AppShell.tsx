import { Box, Button, IconButton, Stack, Typography, useTheme } from '@mui/material'
import type { ReactNode } from 'react'
import { Link as RouterLink, useLocation } from 'react-router'
import { BrandMark } from './BrandMark'
import { UserMenu } from './UserMenu'
import BrightnessMediumIcon from '@mui/icons-material/Brightness4'
import { useAuthStore } from '../store/authStore'
import { useThemeStore } from '../store/themeStore'
import { createGlassTokens } from '../theme/tokens/glass'
import { zIndex } from '../theme/tokens/zIndex'

interface AppShellProps {
  children: ReactNode
  title?: string
  subtitle?: string
  actions?: ReactNode
}

/** Persistent navigation chrome, primarily for authenticated pages (research.md #1) — a
 * sticky, glass-backed top bar (brand mark as the home link, theme toggle, account menu)
 * always present, plus an optional non-glass page title/actions row. Replaced the old
 * `PageHeader` back-link pattern everywhere, including admin (SPEC-017 Phase 8) — with a
 * persistent home link and the account menu carrying every destination, an explicit
 * "back" affordance is redundant.
 *
 * Also used by `PrivacyPage`, which is reachable both signed-in and pre-login — the
 * account menu (Log out, Profile, Settings) would be actively misleading to show a
 * signed-out visitor, so it's swapped for a plain "Sign in" link based on auth state. */
export function AppShell({ children, title, subtitle, actions }: AppShellProps) {
  const theme = useTheme()
  const glass = createGlassTokens(theme.palette.mode)
  const toggleTheme = useThemeStore((s) => s.toggle)
  const isAuthenticated = useAuthStore((s) => Boolean(s.accessToken))
  const { pathname } = useLocation()
  const isHome = pathname === '/studio'

  return (
    <Box sx={{ minHeight: '100dvh', display: 'flex', flexDirection: 'column' }}>
      <Box
        component="header"
        sx={{
          position: 'sticky',
          top: 0,
          zIndex: zIndex.appShell,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          px: 2,
          height: 56,
          bgcolor: glass.background,
          backdropFilter: glass.backdropFilter,
          borderBottom: `1px solid ${glass.border}`,
        }}
      >
        <Stack
          component={RouterLink}
          to="/studio"
          aria-current={isHome ? 'page' : undefined}
          aria-label="Ask Lucy home"
          direction="row"
          spacing={1}
          sx={{ alignItems: 'center', textDecoration: 'none', color: 'text.primary' }}
        >
          <BrandMark size={24} color={theme.palette.primary.main} />
          {/* Branding text inside the persistent home-link, not a document-outline heading —
              subtitle1's default <h6> mapping would otherwise create a heading-order violation
              against every page's own <h5> title (found via WorkflowDesignerPage.a11y.test.tsx). */}
          <Typography
            variant="subtitle1"
            component="span"
            sx={{ fontWeight: 600, display: { xs: 'none', sm: 'block' } }}
          >
            Ask Lucy
          </Typography>
        </Stack>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <IconButton onClick={toggleTheme} aria-label="Toggle theme">
            <BrightnessMediumIcon />
          </IconButton>
          {isAuthenticated ? (
            <UserMenu />
          ) : (
            <Button component={RouterLink} to="/login" size="small">
              Sign in
            </Button>
          )}
        </Stack>
      </Box>

      {(title || actions) && (
        <Stack
          direction="row"
          spacing={1}
          sx={{
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            px: 3,
            pt: 3,
            pb: 1,
          }}
        >
          <Box>
            {title && (
              <Typography variant="h5" sx={{ fontWeight: 600 }}>
                {title}
              </Typography>
            )}
            {subtitle && (
              <Typography variant="body2" color="text.secondary">
                {subtitle}
              </Typography>
            )}
          </Box>
          {actions}
        </Stack>
      )}

      <Box sx={{ flex: 1, minHeight: 0, px: 3, pb: 3 }}>{children}</Box>
    </Box>
  )
}
