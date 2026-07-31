import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { LucyPortrait } from '../features/chat/branding/LucyPortrait'
import { AppFooter } from './AppFooter'
import { BrandMark } from './BrandMark'

interface AuthLayoutProps {
  eyebrow: string
  title: string
  children: ReactNode
}

// A fine grid + long-and-short tick marks along the frame, echoing the ruled
// border of a drawing sheet. Drawn once as a background pattern rather than a
// literal cyanotype blueprint — kept quiet enough to read as texture, not noise.
const draftingPattern = `
  linear-gradient(rgba(247,246,242,0.05) 1px, transparent 1px),
  linear-gradient(90deg, rgba(247,246,242,0.05) 1px, transparent 1px)
`

export function AuthLayout({ eyebrow, title, children }: AuthLayoutProps) {
  return (
    <Box sx={{ display: 'flex', minHeight: '100%', flexDirection: { xs: 'column', md: 'row' } }}>
      {/* Title-block panel — the corner stamp of a drawing sheet: mark, wordmark, tagline,
          and Lucy's portrait (spec 010-lucy-brand-refresh FR-011/FR-015) as a warm focal
          point that counterbalances the panel's otherwise technical/drafting aesthetic. */}
      <Box
        sx={{
          bgcolor: '#171613',
          color: '#F7F6F2',
          flex: { md: '0 0 44%' },
          display: 'flex',
          flexDirection: 'column',
          justifyContent: { xs: 'flex-start', md: 'center' },
          px: { xs: 3, md: 8 },
          py: { xs: 4, md: 0 },
          backgroundImage: draftingPattern,
          backgroundSize: '28px 28px',
          position: 'relative',
        }}
      >
        <Stack spacing={4} sx={{ maxWidth: 380 }}>
          <Stack direction="row" spacing={2.5} sx={{ alignItems: 'center' }}>
            <LucyPortrait variant="auth" alt="Lucy" />
            <BrandMark size={36} color="#D97650" />
          </Stack>
          <Box>
            <Typography variant="h3" sx={{ color: '#F7F6F2', letterSpacing: '-0.01em' }}>
              Ask Lucy
            </Typography>
            <Typography variant="body1" sx={{ color: 'rgba(247,246,242,0.7)', mt: 1.5, lineHeight: 1.6 }}>
              The AI workspace built for people who build things — chat, search your knowledge
              base, and get answers grounded in your own documents.
            </Typography>
          </Box>
        </Stack>
      </Box>

      {/* Form panel */}
      <Box
        sx={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          alignItems: 'center',
          p: { xs: 3, sm: 5 },
          bgcolor: 'background.default',
        }}
      >
        <Box sx={{ width: '100%', maxWidth: 400 }}>
          <Typography variant="overline" color="secondary.main" sx={{ letterSpacing: '0.08em' }}>
            {eyebrow}
          </Typography>
          <Typography variant="h4" sx={{ mt: 0.75, mb: 4, letterSpacing: '-0.01em' }}>
            {title}
          </Typography>
          {children}
        </Box>
        <Box sx={{ mt: 6, width: '100%' }}>
          <AppFooter />
        </Box>
      </Box>
    </Box>
  )
}
