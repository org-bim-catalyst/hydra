import type { ThemeMode } from '../../../store/themeStore'

export interface DotMeshThemeColors {
  idle: string
  reactive: string
}

/** Maps theme mode → the dot mesh's idle/reactive shader colors (spec 010-lucy-brand-refresh
 * FR-008, data-model.md `DotMeshThemeColors`, research.md §2). Deliberately a metallic
 * gold/amber accent rather than the brand's primary/secondary hues — chosen for maximum
 * legibility of the sphere itself against `background.default` in both modes, independent
 * of `theme/tokens/palette.ts` (live user review, 2026-09-06). Kept as a plain function
 * (not a hook) so `ReactiveSphere` can call it directly from inside the R3F tree without a
 * second theme context dependency. */
export function getDotMeshColors(mode: ThemeMode): DotMeshThemeColors {
  return mode === 'dark'
    ? { idle: '#C9A227', reactive: '#FFC94A' } // muted gold / brighter amber-gold
    : { idle: '#8A6B12', reactive: '#B8860B' } // deep bronze-gold / goldenrod
}
