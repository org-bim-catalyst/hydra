import { Box, Link, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'

/**
 * Shared global footer (specs/004-cookie-consent-privacy, FR-010/SC-006) — used by both
 * `AuthLayout` (login/register, so the Privacy link is discoverable pre-login too) and
 * `PrivacyPage` itself (constitution §7: a new shared component needs ≥2 usage sites).
 *
 * Not rendered inside the authenticated chat shell (`ChatPage`): that page is a full-height
 * (`100vh`) flex layout with no footer region, and the authenticated "global navigation"
 * requirement is instead satisfied by the Privacy link already present in every
 * authenticated page's `UserMenu` (research.md Topic 9 — reconciled during implementation).
 */
export function AppFooter() {
  return (
    <Box component="footer" sx={{ px: 3, py: 2, borderTop: 1, borderColor: 'divider' }}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'center', flexWrap: 'wrap' }}>
        <Typography variant="caption" color="text.secondary">
          &copy; {new Date().getFullYear()} Ask Lucy
        </Typography>
        <Link component={RouterLink} to="/privacy" variant="caption" color="text.secondary">
          Privacy Policy
        </Link>
      </Stack>
    </Box>
  )
}
