export interface MotionTokens {
  duration: { fast: number; standard: number; slow: number }
  easing: { standard: string; decelerate: string; accelerate: string }
}

const standardDuration = { fast: 120, standard: 200, slow: 320 }
const reducedDuration = { fast: 0, standard: 0, slow: 0 }

const easing = {
  standard: 'cubic-bezier(0.4, 0, 0.2, 1)',
  decelerate: 'cubic-bezier(0, 0, 0.2, 1)',
  accelerate: 'cubic-bezier(0.4, 0, 1, 1)',
}

/** Centralized transition timing (FR-006, FR-010). When `prefersReducedMotion` is true,
 * durations collapse to 0 so consumers (including MUI's own Dialog/Drawer/Menu/Collapse
 * transitions wired through `theme/index.ts`) skip animation without any per-component
 * reduced-motion branching. */
export function createMotionTokens(prefersReducedMotion: boolean): MotionTokens {
  return {
    duration: prefersReducedMotion ? reducedDuration : standardDuration,
    easing,
  }
}
