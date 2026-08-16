/**
 * Public-facing Flumeria visual identity (landing + auth-flow pages only) — adapted from
 * the supplied Readdy.ai reference design. Deliberately scoped to this feature area rather
 * than the app-wide MUI theme (theme/tokens/palette.ts): the authenticated workspace keeps
 * its existing "drafting table" graphite/ink-blue identity unchanged (spec.md Assumptions —
 * "do not redesign the workspace except where necessary"), while the public storefront gets
 * its own distinct, green-led identity matching the reference — the same pattern many
 * products use (marketing site vs. in-app UI look different but share a brand).
 */
export const flumeriaColor = {
  green: '#15803D',
  greenDark: '#116932',
  greenLight: '#DCFCE7',
  greenLightText: '#166534',
  black: '#0A0A0A',
  offWhite: '#FAFAF8',
  white: '#FFFFFF',
  heading: '#171717',
  body: '#4B5563',
  bodyOnDark: 'rgba(247, 246, 242, 0.75)',
  inputFill: '#F0F0EE',
  border: '#E5E5E0',
} as const

export const flumeriaRadius = {
  button: 10,
  pill: 999,
  panel: 16,
} as const
