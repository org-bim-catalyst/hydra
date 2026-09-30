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

/** A corner outline with a new point on its edge and a plus: "add a corner". */
export function AddCornerIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <path d="M3 20 V4 H21" />
      <circle cx="12" cy="4" r="2" fill="currentColor" stroke="none" />
      <path d="M12 12v6M9 15h6" />
    </svg>
  )
}

/** A corner outline with a cross: "delete the selected corner(s)". */
export function DeleteCornerIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <path d="M3 20 V4 H21" />
      <circle cx="12" cy="4" r="2" fill="currentColor" stroke="none" />
      <path d="M9.5 12.5l5 5M14.5 12.5l-5 5" />
    </svg>
  )
}

/** A square outline with a shaded circle overlapping it and a plus: "add a circle to the outline". */
export function AddCircleShapeIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <rect x="3" y="3" width="12" height="12" rx="1" />
      <circle cx="15" cy="15" r="6.5" fill="currentColor" fillOpacity="0.25" />
      <path d="M15 12v6M12 15h6" />
    </svg>
  )
}

/** A square outline with a dashed circle overlapping it and a minus: "cut a circle out of the outline". */
export function CutCircleShapeIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <rect x="3" y="3" width="12" height="12" rx="1" />
      <circle cx="15" cy="15" r="6.5" strokeDasharray="3 2.5" />
      <path d="M12 15h6" />
    </svg>
  )
}

/** A square outline turning into a circle: "make the whole ring a circle". */
export function RingToCircleIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <rect x="3" y="3" width="18" height="18" rx="1" strokeDasharray="3 3" />
      <circle cx="12" cy="12" r="6.5" />
    </svg>
  )
}

/** An arc through three dropped points: "draw an arc". */
export function DrawArcIcon({ size = 24 }: IconProps) {
  return (
    <svg {...common} width={size} height={size}>
      <path d="M3 19 A 11 11 0 0 1 21 19" />
      <circle cx="3" cy="19" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="12" cy="7.5" r="1.6" fill="currentColor" stroke="none" />
      <circle cx="21" cy="19" r="1.6" fill="currentColor" stroke="none" />
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
