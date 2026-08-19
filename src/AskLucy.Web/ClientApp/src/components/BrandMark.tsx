interface BrandMarkProps {
  size?: number
  color?: string
  /** Background swatch behind the mark — pass the Flumeria green for the rounded-square
   * badge treatment used on the public landing/auth pages; omit for a bare mark (e.g. on
   * a dark scrim where the badge would be redundant). */
  background?: string
}

function MarkGlyph({ color }: { color: string }) {
  return (
    <g transform="translate(3 5)">
      <rect x="0" y="8" width="9" height="12" rx="1.5" fill={color} opacity="0.9" />
      <rect x="12" y="4" width="8" height="16" rx="1.5" fill={color} opacity="0.9" />
      <rect x="25" y="10" width="9" height="10" rx="1.5" fill={color} opacity="0.9" />
      <path
        d="M0 30 C8 26, 12 34, 18 30 C24 26, 28 34, 36 30"
        stroke={color}
        strokeWidth="3"
        strokeLinecap="round"
        fill="none"
      />
      <circle cx="36" cy="30" r="2.5" fill={color} />
    </g>
  )
}

/**
 * The Flumeria mark: a winding river/site-path threading through a cluster of massing
 * blocks — a nod to urban design worked over spatial/site data, replacing the earlier
 * compass-seal "L" mark (which read as an unrelated initial, not the Flumeria brand).
 */
export function BrandMark({ size = 40, color = 'currentColor', background }: BrandMarkProps) {
  if (!background) {
    return (
      <svg width={size} height={size} viewBox="0 0 48 48" fill="none" role="img" aria-label="Flumeria">
        <MarkGlyph color={color} />
      </svg>
    )
  }

  return (
    <svg width={size} height={size} viewBox="0 0 48 48" role="img" aria-label="Flumeria">
      <rect width="48" height="48" rx="12" fill={background} />
      <MarkGlyph color={color} />
    </svg>
  )
}
