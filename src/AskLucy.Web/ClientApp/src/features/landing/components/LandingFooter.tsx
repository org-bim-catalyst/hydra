import { Box, Link, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { BrandMark } from '../../../components/BrandMark'
import { flumeriaColor } from '../theme/flumeriaPalette'

/**
 * Black footer, matching the reference design. Landing-page-specific (not the shared
 * `AppFooter`, which keeps its existing white/bordered style for the auth pages and
 * `PrivacyPage` — this is a distinct visual treatment for the public landing page only).
 * Links to what actually exists in this app (`/privacy`); the reference's "About"/"Blog"
 * are omitted rather than becoming dead links.
 */
export function LandingFooter() {
  return (
    <Box component="footer" sx={{ bgcolor: flumeriaColor.black, px: { xs: 3, sm: 6, md: 10 }, py: 4 }}>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <BrandMark size={22} color={flumeriaColor.green} />
          {/* component="span": subtitle2 defaults to <h6>; this is a footer brand label,
              not a document-outline heading (axe heading-order). */}
          <Typography variant="subtitle2" component="span" sx={{ color: flumeriaColor.white, fontWeight: 700 }}>
            Flumeria
          </Typography>
        </Stack>
        <Stack direction="row" spacing={3} sx={{ alignItems: 'center' }}>
          <Link component={RouterLink} to="/privacy" variant="body2" sx={{ color: flumeriaColor.bodyOnDark }}>
            Privacy
          </Link>
          <Typography variant="caption" sx={{ color: flumeriaColor.bodyOnDark }}>
            &copy; {new Date().getFullYear()} Flumeria. All rights reserved.
          </Typography>
        </Stack>
      </Stack>
    </Box>
  )
}
