import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { BrandMark } from './BrandMark'
import { LucyPortrait } from '../features/chat/branding/LucyPortrait'
import { authBranding } from '../features/landing/content/copy'
import { flumeriaColor, flumeriaRadius } from '../features/landing/theme/flumeriaPalette'
import { AppFooter } from './AppFooter'

interface AuthLayoutProps {
  title: string
  /** One-line description under the title, matching the reference's subtitle under
   * "Welcome back" / "Create your account". */
  subtitle?: string
  /** Overlay copy on the left panel; falls back to a generic Flumeria line for the
   * secondary auth-flow pages (confirm-email, external-login) that don't have their own
   * tagline in `authBranding`. */
  tagline?: string
  /** Left-panel background image; falls back to the sign-in reference image for the
   * secondary auth-flow pages that don't pass one explicitly. */
  image?: string
  children: ReactNode
}

const DEFAULT_TAGLINE = 'Design better urban spaces with AI.'

/**
 * Split-screen auth shell used by every auth-flow page (sign-in, sign-up, email
 * confirmation, email-change confirmation, external-login completion — spec.md FR-007).
 * Visual language and imagery taken directly from the supplied Readdy.ai reference
 * design's sign-in/sign-up pages (research.md Topic 3) — the left-panel photo is the
 * reference's own source image, downloaded directly rather than recreated.
 */
export function AuthLayout({ title, subtitle, tagline, image, children }: AuthLayoutProps) {
  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', flexDirection: { xs: 'column', md: 'row' } }}>
      {/* Photo panel */}
      <Box sx={{ position: 'relative', flex: { md: '0 0 46%' }, minHeight: { xs: 220, md: 'auto' }, overflow: 'hidden' }}>
        <Box
          component="img"
          src={image ?? authBranding.signIn.image}
          alt=""
          sx={{ position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'cover' }}
        />
        <Box
          aria-hidden="true"
          sx={{
            position: 'absolute',
            inset: 0,
            background: 'linear-gradient(180deg, rgba(10,10,10,0.15) 0%, rgba(10,10,10,0.35) 55%, rgba(10,10,10,0.85) 100%)',
          }}
        />
        <Stack direction="row" spacing={2} sx={{ position: 'absolute', left: 0, right: 0, bottom: 0, px: { xs: 3, md: 5 }, py: { xs: 3, md: 5 }, alignItems: 'flex-end' }}>
          {/* Lucy's portrait (spec 010-lucy-brand-refresh FR-011/FR-013 — preserved here,
              not dropped, alongside the new Flumeria brand overlay) */}
          <LucyPortrait variant="toggle" alt="Lucy" />
          <Stack spacing={1.25}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <BrandMark size={24} color={flumeriaColor.white} />
              <Typography variant="subtitle1" sx={{ color: flumeriaColor.white, fontWeight: 700 }}>
                Flumeria
              </Typography>
            </Stack>
            <Typography variant="body2" sx={{ color: 'rgba(255,255,255,0.85)', maxWidth: 360 }}>
              {tagline ?? DEFAULT_TAGLINE}
            </Typography>
          </Stack>
        </Stack>
      </Box>

      {/* Form panel — TextField/Button overrides below apply the Flumeria style to every
          auth page's form without needing per-page changes (constitution §III DRY). */}
      <Box
        sx={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          alignItems: 'center',
          p: { xs: 3, sm: 5 },
          bgcolor: flumeriaColor.white,
          '& .MuiOutlinedInput-root': {
            bgcolor: flumeriaColor.inputFill,
            borderRadius: `${flumeriaRadius.button}px`,
            '& fieldset': { borderColor: 'transparent' },
            '&:hover fieldset': { borderColor: flumeriaColor.border },
            '&.Mui-focused fieldset': { borderColor: flumeriaColor.green },
            // Browser autofill (Chrome/Edge) paints its own background via the
            // :-webkit-autofill pseudo-class at a specificity our bgcolor can't beat, and
            // does so without firing React's onChange — so MUI never learns the field is
            // "filled" and leaves the label overlapping the value. The inset box-shadow
            // trick repaints over the browser's autofill background instead of fighting it.
            '& input:-webkit-autofill': {
              WebkitBoxShadow: `0 0 0 100px ${flumeriaColor.inputFill} inset`,
              WebkitTextFillColor: flumeriaColor.heading,
              caretColor: flumeriaColor.heading,
              borderRadius: 'inherit',
            },
            '& input:-webkit-autofill:hover, & input:-webkit-autofill:focus': {
              WebkitBoxShadow: `0 0 0 100px ${flumeriaColor.inputFill} inset`,
            },
            '& input::placeholder, & textarea::placeholder': {
              color: flumeriaColor.body,
              opacity: 0.7,
            },
          },
          '& .MuiButton-contained': {
            bgcolor: flumeriaColor.green,
            borderRadius: `${flumeriaRadius.button}px`,
            '&:hover': { bgcolor: flumeriaColor.greenDark },
          },
          '& .MuiButton-outlined': {
            borderRadius: `${flumeriaRadius.button}px`,
            borderColor: flumeriaColor.border,
            color: flumeriaColor.heading,
          },
          '& a': { color: flumeriaColor.green },
        }}
      >
        <Box sx={{ width: '100%', maxWidth: 400 }}>
          <Typography variant="h4" sx={{ letterSpacing: '-0.01em', color: flumeriaColor.heading, fontWeight: 800 }}>
            {title}
          </Typography>
          {subtitle && (
            <Typography variant="body1" sx={{ mt: 1, mb: 4, color: flumeriaColor.body }}>
              {subtitle}
            </Typography>
          )}
          {!subtitle && <Box sx={{ mb: 4 }} />}
          {children}
        </Box>
        <Box sx={{ mt: 6, width: '100%' }}>
          <AppFooter textColor={flumeriaColor.body} />
        </Box>
      </Box>
    </Box>
  )
}
