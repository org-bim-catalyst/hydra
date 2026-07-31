import PersonIcon from '@mui/icons-material/Person'
import { Avatar, Box } from '@mui/material'
import { useState } from 'react'
import lucyPortrait from '../../../assets/branding/lucy-portrait.png'

export type LucyPortraitVariant = 'toggle' | 'auth'

interface LucyPortraitProps {
  /** Controls sizing/framing only — every variant renders the same source image
   * (data-model.md `LucyPortraitAsset`, research.md §4). */
  variant: LucyPortraitVariant
  /** Required, no default — forces every call site to supply context-appropriate alt
   * text (FR-013) rather than silently falling back to an empty/generic label. */
  alt: string
}

const SIZE_BY_VARIANT: Record<LucyPortraitVariant, number> = {
  toggle: 40,
  auth: 96,
}

/** Shared Lucy character portrait (spec 010-lucy-brand-refresh FR-010–014) — one
 * component, one asset, so every consumer (chat toggle, auth pages) renders "the same
 * character, consistent framing" (User Story 3) instead of duplicating alt text and
 * failure handling per call site. Falls back to a generic avatar icon if the asset fails
 * to load (FR-014), never a broken-image icon. */
export function LucyPortrait({ variant, alt }: LucyPortraitProps) {
  const [failed, setFailed] = useState(false)
  const size = SIZE_BY_VARIANT[variant]

  if (failed) {
    return (
      <Avatar sx={{ width: size, height: size, bgcolor: 'primary.main' }} aria-label={alt}>
        <PersonIcon fontSize={variant === 'toggle' ? 'medium' : 'large'} />
      </Avatar>
    )
  }

  return (
    <Box
      component="img"
      src={lucyPortrait}
      alt={alt}
      onError={() => setFailed(true)}
      sx={{
        width: size,
        height: size,
        borderRadius: '50%',
        objectFit: 'cover',
        display: 'block',
        border: variant === 'auth' ? '2px solid rgba(247,246,242,0.25)' : 'none',
      }}
    />
  )
}
