import { Box, Stack, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { Link as RouterLink } from 'react-router'
import type { ReactNode } from 'react'
import { BrandMark } from '../../../components/BrandMark'
import { hero, heroImages } from '../content/copy'
import { flumeriaColor } from '../theme/flumeriaPalette'

interface LandingHeroProps {
  /** The hero's own two actions ("Start Designing →" / "Explore Flumeria") — matching the
   * reference, the nav carries only the brand mark; Sign In is reached via the sign-up
   * page's own "Already have an account?" link (FR-003 is satisfied end-to-end, not via a
   * standalone nav-level CTA). */
  ctaBar: ReactNode
}

const ROTATE_INTERVAL_MS = 6000

/**
 * Full-bleed, full-height hero matching the reference design: a minimal top nav (brand
 * identity + Sign In/Sign Up, FR-014 — no dashboard-style nav) over a rotating full-bleed
 * background (the reference uses a 5-image carousel, indicated by dots) with a dark scrim
 * for text legibility. Images are the reference's own source images, downloaded directly
 * rather than recreated (research.md Topic 3).
 */
export function LandingHero({ ctaBar }: LandingHeroProps) {
  const [activeIndex, setActiveIndex] = useState(0)

  useEffect(() => {
    const id = setInterval(() => {
      setActiveIndex((i) => (i + 1) % heroImages.length)
    }, ROTATE_INTERVAL_MS)
    return () => clearInterval(id)
  }, [])

  return (
    <Box
      component="header"
      sx={{
        position: 'relative',
        overflow: 'hidden',
        minHeight: '100vh',
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      <Box aria-hidden="true" sx={{ position: 'absolute', inset: 0 }}>
        {heroImages.map((src, index) => (
          <Box
            key={src}
            component="img"
            src={src}
            alt=""
            sx={{
              position: 'absolute',
              inset: 0,
              width: '100%',
              height: '100%',
              objectFit: 'cover',
              opacity: index === activeIndex ? 1 : 0,
              transition: 'opacity 1200ms ease',
            }}
          />
        ))}
        <Box
          sx={{
            position: 'absolute',
            inset: 0,
            background: 'linear-gradient(180deg, rgba(10,10,10,0.4) 0%, rgba(10,10,10,0.35) 45%, rgba(10,10,10,0.85) 100%)',
          }}
        />
      </Box>

      <Stack
        direction="row"
        sx={{ position: 'relative', alignItems: 'center', px: { xs: 3, sm: 6, md: 10 }, py: 3 }}
      >
        <Stack component={RouterLink} to="/" direction="row" spacing={1} sx={{ alignItems: 'center', textDecoration: 'none' }}>
          <BrandMark size={26} color={flumeriaColor.white} />
          <Typography variant="subtitle1" sx={{ color: flumeriaColor.white, fontWeight: 700 }}>
            Flumeria
          </Typography>
        </Stack>
      </Stack>

      <Stack spacing={3} sx={{ position: 'relative', flex: 1, justifyContent: 'center', maxWidth: 640, px: { xs: 3, sm: 6, md: 10 } }}>
        <Typography
          variant="overline"
          sx={{ color: 'rgba(255,255,255,0.85)', letterSpacing: '0.16em', fontWeight: 600 }}
        >
          {hero.eyebrow}
        </Typography>
        <Typography component="h1" variant="h2" sx={{ color: flumeriaColor.white, fontWeight: 800, letterSpacing: '-0.01em', lineHeight: 1.1 }}>
          {hero.headline}
        </Typography>
        <Typography variant="h6" component="p" sx={{ color: 'rgba(255,255,255,0.85)', fontWeight: 400, lineHeight: 1.6 }}>
          {hero.subhead}
        </Typography>
        {ctaBar}
      </Stack>

      {/* Carousel dots, matching the reference — decorative + a real control */}
      <Stack
        direction="row"
        spacing={1}
        role="tablist"
        aria-label="Hero background image"
        sx={{ position: 'relative', justifyContent: 'center', py: { xs: 3, md: 4 } }}
      >
        {heroImages.map((src, index) => (
          <Box
            key={src}
            component="button"
            type="button"
            role="tab"
            aria-selected={index === activeIndex}
            aria-label={`Show background image ${index + 1}`}
            onClick={() => setActiveIndex(index)}
            sx={{
              width: index === activeIndex ? 20 : 8,
              height: 8,
              borderRadius: 999,
              border: 'none',
              bgcolor: index === activeIndex ? flumeriaColor.white : 'rgba(255,255,255,0.4)',
              transition: 'width 200ms ease, background-color 200ms ease',
              cursor: 'pointer',
              p: 0,
            }}
          />
        ))}
      </Stack>
    </Box>
  )
}
