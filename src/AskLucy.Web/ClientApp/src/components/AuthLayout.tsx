import { Box, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
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
      {/* Title-block panel — the corner stamp of a drawing sheet: mark, wordmark, tagline. */}
      <Box
        sx={{
          bgcolor: '#171613',
          color: '#F7F6F2',
          flex: { md: '0 0 42%' },
          display: 'flex',
          flexDirection: 'column',
          justifyContent: { xs: 'flex-start', md: 'center' },
          px: { xs: 3, md: 7 },
          py: { xs: 4, md: 0 },
          backgroundImage: draftingPattern,
          backgroundSize: '28px 28px',
          position: 'relative',
        }}
      >
        <Stack spacing={3} sx={{ maxWidth: 360 }}>
          <BrandMark size={40} color="#D97650" />
          <Box>
            <Typography variant="h3" sx={{ color: '#F7F6F2' }}>
              Ask Lucy
            </Typography>
            <Typography variant="body2" sx={{ color: 'rgba(247,246,242,0.65)', mt: 1 }}>
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
        <Box sx={{ width: '100%', maxWidth: 380 }}>
          <Typography variant="overline" color="secondary.main">
            {eyebrow}
          </Typography>
          <Typography variant="h4" sx={{ mt: 0.5, mb: 4 }}>
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
