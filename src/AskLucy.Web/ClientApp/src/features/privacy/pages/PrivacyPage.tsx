import { Box, Divider, Link, Paper, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { AppFooter } from '../../../components/AppFooter'
import { AppShell } from '../../../components/AppShell'
import { COOKIE_CATEGORIES } from '../../consent/cookieCategories'
import { useAuthStore } from '../../../store/authStore'
import { useCookiePolicy } from '../hooks/useCookiePolicy'

/**
 * Public Privacy Page (specs/004-cookie-consent-privacy, FR-009). Reachable without
 * authentication — the route is registered outside `ProtectedRoute` (router.tsx). Content
 * is English-only static copy at initial launch (FR-021); the policy version/effective
 * date below is the one value fetched live, so it can never drift from the same source of
 * truth the re-consent logic uses (research.md Topic 8).
 */
export function PrivacyPage() {
  const { data: policy } = useCookiePolicy()
  const isAuthenticated = useAuthStore((s) => Boolean(s.accessToken))

  return (
    <AppShell title="Privacy Policy">
      <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100%' }}>
        <Box sx={{ flex: 1 }}>
          <Paper elevation={1} sx={{ maxWidth: 720, p: { xs: 3, sm: 4 } }}>
            <Stack spacing={3}>
              {policy && (
                <Typography variant="body2" color="text.secondary">
                  Policy version {policy.version} — effective{' '}
                  {new Date(policy.effectiveAtUtc).toLocaleDateString()}
                </Typography>
              )}

              <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Cookie categories
                </Typography>
                <Stack spacing={1.5}>
                  {COOKIE_CATEGORIES.map((category) => (
                    <Box key={category.key}>
                      <Typography variant="body2" sx={{ fontWeight: 600 }}>
                        {category.label}
                      </Typography>
                      <Typography variant="body2" color="text.secondary">
                        {category.description}
                      </Typography>
                    </Box>
                  ))}
                </Stack>
              </Box>

              <Divider />

              <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  What data we collect
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Account information you provide (name, email), your conversations and files with
                  Ask Lucy, and — only where you've enabled the relevant category above — usage
                  analytics and marketing interaction data.
                </Typography>
              </Box>

              <Divider />

              <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Third-party services
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  We use AI providers to generate responses, and, where enabled, analytics and
                  marketing tools to understand and improve the product. None of your data is sold
                  to third parties.
                </Typography>
              </Box>

              <Divider />

              <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Data retention
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Your cookie consent decisions are retained for the lifetime of your account, for
                  audit and compliance purposes, and follow the same retention and deletion rules as
                  the rest of your account data.
                </Typography>
              </Box>

              <Divider />

              <Box>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Manage your preferences
                </Typography>
                {isAuthenticated ? (
                  <Typography variant="body2">
                    You can change your cookie preferences at any time from{' '}
                    <Link component={RouterLink} to="/settings">
                      Settings &gt; Cookies
                    </Link>
                    .
                  </Typography>
                ) : (
                  <Typography variant="body2">
                    <Link component={RouterLink} to="/login">
                      Sign in
                    </Link>{' '}
                    to manage your cookie preferences from Settings.
                  </Typography>
                )}
              </Box>
            </Stack>
          </Paper>
        </Box>
        <AppFooter />
      </Box>
    </AppShell>
  )
}
