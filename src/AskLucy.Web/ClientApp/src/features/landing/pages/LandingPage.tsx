import { Box } from '@mui/material'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { FeatureBlocksSection } from '../components/FeatureBlocksSection'
import { HowItWorksSection } from '../components/HowItWorksSection'
import { LandingCtaBar } from '../components/LandingCtaBar'
import { LandingFooter } from '../components/LandingFooter'
import { LandingHero } from '../components/LandingHero'
import { NewsletterSection } from '../components/NewsletterSection'
import { ScrollToTopButton } from '../components/ScrollToTopButton'
import { StatsSection } from '../components/StatsSection'
import { meta } from '../content/copy'

/**
 * Public marketing landing page (spec.md FR-001/FR-002/FR-014). Structure, imagery, and
 * copy adapted directly from the supplied Readdy.ai reference design (research.md Topic 3):
 * hero → how-it-works → five feature blocks → stats band → newsletter band → footer.
 * Wrapped in `PublicConsentGate` (FR-020), carries its own SEO/social-preview metadata via
 * React 19's native `<title>`/`<meta>` hoisting (research.md Topic 1, FR-022). Navigation
 * is intentionally limited to brand identity plus Sign In/Sign Up in `LandingHero`'s own nav
 * row, and the hero's own "Start Designing"/"Explore Flumeria" actions (FR-003/FR-014) — no
 * dashboard-style nav.
 */
export function LandingPage() {
  return (
    <PublicConsentGate>
      <title>{meta.title}</title>
      <meta name="description" content={meta.description} />
      <meta property="og:title" content={meta.title} />
      <meta property="og:description" content={meta.description} />
      <meta property="og:type" content="website" />

      <Box component="main">
        <LandingHero ctaBar={<LandingCtaBar />} />
        <HowItWorksSection />
        <FeatureBlocksSection />
        <StatsSection />
        <NewsletterSection />
      </Box>

      <LandingFooter />
      <ScrollToTopButton />
    </PublicConsentGate>
  )
}
