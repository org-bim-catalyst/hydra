import { Box, Link, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'

interface AppFooterProps {
  /** Overrides the theme's mode-dependent `text.secondary` (tuned for dark-mode
   * backgrounds) with a fixed color — needed on `AuthLayout`, which paints its panel white
   * regardless of the app's light/dark setting, so the ambient dark-mode token would render
   * as pale, low-contrast text on that white surface. */
  textColor?: string
}

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
export function AppFooter({ textColor }: AppFooterProps) {
  return (
    <Box component="footer" sx={{ px: 3, py: 2, borderTop: 1, borderColor: 'divider' }}>
      <Stack direction="row" spacing={2} sx={{ alignItems: 'center', justifyContent: 'center', flexWrap: 'wrap' }}>
        <Typography variant="caption" sx={{ color: textColor ?? 'text.secondary' }}>
          &copy; {new Date().getFullYear()} Flumeria
        </Typography>
        {/* No color override here even when `textColor` is set: this stays a link, whose
            color the caller's own link styling (e.g. AuthLayout's `& a` rule) already
            handles — overriding it here would fight that instead of complementing it. */}
        <Link component={RouterLink} to="/privacy" variant="caption" color="text.secondary">
          Privacy Policy
        </Link>
      </Stack>
    </Box>
  )
}
