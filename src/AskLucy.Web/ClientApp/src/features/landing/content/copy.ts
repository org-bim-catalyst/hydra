import hero1 from '../../../assets/landing/hero-1.png'
import hero2 from '../../../assets/landing/hero-2.png'
import hero3 from '../../../assets/landing/hero-3.png'
import hero4 from '../../../assets/landing/hero-4.png'
import hero5 from '../../../assets/landing/hero-5.png'
import featureSiteIntelligence from '../../../assets/landing/feature-site-intelligence.png'
import featureUrbanContext from '../../../assets/landing/feature-urban-context.png'
import featureDesignAnalysis from '../../../assets/landing/feature-design-analysis.png'
import featureAiInsights from '../../../assets/landing/feature-ai-insights.png'
import featureDesignEvaluation from '../../../assets/landing/feature-design-evaluation.png'
import authSignup from '../../../assets/landing/auth-signup.png'
import authSignin from '../../../assets/landing/auth-signin.png'

/**
 * Centralized landing/auth-page copy (spec.md FR-002, constitution §7 — user-facing
 * strings kept out of components so i18n extraction stays mechanical later). Structure,
 * imagery, and content are taken directly from the supplied Readdy.ai reference design
 * (landing/sign-up/sign-in previews) — images downloaded from the reference's own image
 * source URLs (research.md Topic 3), copy transcribed from the rendered pages, not
 * invented independently.
 */

/** The reference hero is a 5-image rotating background (carousel dots visible in the
 * original design) — all five are used by `LandingHero`. */
export const heroImages = [hero1, hero2, hero3, hero4, hero5]

export const hero = {
  eyebrow: 'AI-Powered Urban Design Intelligence',
  headline: 'Design Better Urban Spaces With AI',
  subhead:
    'Flumeria analyzes sites, understands urban context, and generates data-driven design insights — with Lucy, the AI engine behind every recommendation.',
}

export const howItWorks = {
  eyebrow: 'How It Works',
  title: 'The Flumeria Workflow',
  steps: [
    {
      number: '01',
      icon: 'search' as const,
      title: 'Discover',
      body: 'Identify and explore a site using satellite imagery, geographic data, and environmental context.',
    },
    {
      number: '02',
      icon: 'analyze' as const,
      title: 'Analyze',
      body: 'Lucy automatically analyzes surrounding urban context — roads, buildings, amenities, green spaces, and transportation networks.',
    },
    {
      number: '03',
      icon: 'evaluate' as const,
      title: 'Evaluate',
      body: 'Evaluate design opportunities and constraints against multiple urban design and sustainability criteria.',
    },
    {
      number: '04',
      icon: 'design' as const,
      title: 'Design',
      body: 'Turn complex spatial data into actionable, data-driven design recommendations and strategies.',
    },
  ],
}

export interface FeatureBlock {
  key: string
  icon: 'pin' | 'building' | 'gauge' | 'insights' | 'check'
  heading: string
  body: string
  tags: readonly string[]
  image: string
  imageSide: 'left' | 'right'
}

const commonTags = ['Spatial Analysis', 'GIS Integration', 'AI-Powered'] as const

/**
 * The five alternating image/text feature blocks (spec.md FR-002's "how GIS/maps/2D-3D
 * models/spatial analysis are integrated" and "how AI-generated analysis is visualized"
 * topics) — same five blocks, same order, same images as the reference design.
 *
 * Correction: an earlier pass mistakenly split the reference's single "Design Evaluation"
 * block (which itself already contains a side-by-side design-comparison image) into a
 * fabricated sixth "Design Comparison" block with duplicated copy. The reference has five
 * blocks, not six — confirmed by directly downloading the reference's five distinct
 * `feature-*` image URLs (site-intel, urban-context, design-analysis, ai-insights,
 * design-eval) rather than relying on a mid-scroll screenshot.
 */
export const featureBlocks: readonly FeatureBlock[] = [
  {
    key: 'site-intelligence',
    icon: 'pin',
    heading: 'Site Intelligence',
    body: 'Analyze a site and automatically understand its surrounding context. Lucy processes satellite imagery, terrain data, solar exposure, wind patterns, and ecological factors to build a comprehensive site profile — in minutes, not weeks.',
    tags: commonTags,
    image: featureSiteIntelligence,
    imageSide: 'left',
  },
  {
    key: 'urban-context',
    icon: 'building',
    heading: 'Urban Context',
    body: 'Understand surrounding roads, spaces, transportation networks, and urban relationships. Flumeria maps the complete urban fabric around your site — revealing patterns invisible to traditional analysis.',
    tags: commonTags,
    image: featureUrbanContext,
    imageSide: 'right',
  },
  {
    key: 'design-analysis',
    icon: 'gauge',
    heading: 'Design Analysis',
    body: 'Evaluate a site against multiple urban-design criteria — accessibility, connectivity, biodiversity potential, microclimate, social equity, and more. Lucy scores and benchmarks your site against successful projects worldwide.',
    tags: commonTags,
    image: featureDesignAnalysis,
    imageSide: 'left',
  },
  {
    key: 'ai-insights',
    icon: 'insights',
    heading: 'AI Insights',
    body: 'Turn complex spatial data into understandable design recommendations. Lucy doesn’t just analyze — it synthesizes geographic, environmental, and urban data into clear, actionable insights that guide your design decisions with data-driven confidence.',
    tags: commonTags,
    image: featureAiInsights,
    imageSide: 'right',
  },
  {
    key: 'design-evaluation',
    icon: 'check',
    heading: 'Design Evaluation',
    body: 'Compare alternative design strategies using measurable criteria. Test different layouts, planting strategies, and programming scenarios against the same objective framework — let data guide your creative decisions.',
    tags: commonTags,
    image: featureDesignEvaluation,
    imageSide: 'left',
  },
]

export const stats = [
  { value: '98%', label: 'Analysis Accuracy' },
  { value: '10x', label: 'Faster Site Analysis' },
  { value: '50K+', label: 'Sites Analyzed' },
  { value: '120+', label: 'Countries' },
]

export const newsletter = {
  title: 'Stay Informed',
  body: 'Get the latest on AI-powered urban design, product updates, and insights from the frontier of landscape architecture technology.',
  placeholder: 'Your email address',
  cta: 'Subscribe',
  confirmation: "Thanks — you're on the list.",
}

export const cta = {
  signIn: 'Sign In',
  signUp: 'Create Account / Sign Up',
  tryPlatform: 'Try the Platform',
}

export const meta = {
  title: 'Flumeria — AI-Assisted Urban Design Platform',
  description:
    'Flumeria brings GIS, maps, and 2D/3D models into one AI-assisted workspace. Lucy reads your site data and helps design, analyze, and communicate urban design decisions.',
}

export const authBranding = {
  signUp: {
    tagline: 'Join thousands of landscape architects and designers using AI to design better parks and public spaces.',
    subtitle: 'Start designing better urban spaces with AI-powered intelligence.',
    image: authSignup,
  },
  signIn: {
    tagline: 'Design better parks with AI. Sign in to access your projects, insights, and design intelligence.',
    subtitle: 'Sign in to continue to your Flumeria workspace.',
    image: authSignin,
  },
}
