interface BrandMarkProps {
  size?: number
  color?: string
}

/**
 * The Ask Lucy mark: a compass-seal ring (a nod to the drafting instruments of
 * the BIM/AEC audience) with an "L" cut from two square-cornered strokes, and a
 * single pivot dot standing in for a dimension-line terminus. Deliberately not
 * a rounded, friendly "AI sparkle" mark — this reads as instrument-precise.
 */
export function BrandMark({ size = 40, color = 'currentColor' }: BrandMarkProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 48 48" fill="none" role="img" aria-label="Ask Lucy">
      <circle cx="24" cy="24" r="19.5" stroke={color} strokeWidth="1.5" />
      <path
        d="M18 15V32H30"
        stroke={color}
        strokeWidth="3"
        strokeLinecap="square"
        strokeLinejoin="miter"
      />
      <circle cx="33.5" cy="14.5" r="2.25" fill={color} />
    </svg>
  )
}
