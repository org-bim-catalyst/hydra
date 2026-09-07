import type { ThemeMode } from '../../../store/themeStore'

export interface DotMeshThemeColors {
  idle: string
  reactive: string
}

/** Maps theme mode → the dot mesh's idle/reactive shader colors (spec 010-lucy-brand-refresh
 * FR-008, data-model.md `DotMeshThemeColors`, research.md §2). Deliberately an electric
 * cyan/teal accent rather than the brand's primary/secondary hues — chosen for maximum
 * legibility of the sphere itself against `background.default` in both modes, independent
 * of `theme/tokens/palette.ts` (live user review, 2026-09-06 — superseded an earlier
 * gold/amber pass once the sphere.frag.glsl rim-lighting change shipped alongside it). Kept
 * as a plain function (not a hook) so `ReactiveSphere` can call it directly from inside the
 * R3F tree without a second theme context dependency. */
export function getDotMeshColors(mode: ThemeMode): DotMeshThemeColors {
  return mode === 'dark'
    ? { idle: '#22D3EE', reactive: '#A5FFFB' } // electric cyan / bright aqua-white
    : { idle: '#0E7490', reactive: '#0891B2' } // deep teal / brighter cyan
}
