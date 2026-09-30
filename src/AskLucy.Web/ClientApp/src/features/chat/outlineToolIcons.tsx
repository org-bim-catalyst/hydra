/**
 * specs/079: the two Outline tools with no fitting icon in the Remix set. Drawn to match Remix's
 * 24 x 24 outline style (2 px strokes, round caps), and sized and coloured like them.
 */
interface IconProps {
  size?: number
}

const common = { viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round', 'aria-hidden': true } as const

/** A dashed selection box with a corner point inside it: "select corners". */
export function SelectCornersIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <rect x="3" y="3" width="18" height="18" rx="1" strokeDasharray="3 3" />
      <circle cx="9" cy="9" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="15" cy="14" r="1.6" fill="currentColor" stroke="none" />
    </svg>
  )
}

/** A straight edge bent into an arc between two end points: "curve this edge". */
export function CurveEdgeIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <path d="M4 18 Q12 2 20 18" />
      <circle cx="4" cy="18" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="20" cy="18" r="1.6" fill="currentColor" stroke="none" />
    </svg>
  )
}
