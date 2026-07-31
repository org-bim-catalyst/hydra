import type { ThemeMode } from '../../../store/themeStore'

export interface DotMeshThemeColors {
  idle: string
  reactive: string
}

/** Maps theme mode → the dot mesh's idle/reactive shader colors (spec 010-lucy-brand-refresh
 * FR-008, data-model.md `DotMeshThemeColors`, research.md §2). Derived from the *same*
 * `primary`/`secondary` brand tokens `theme/tokens/palette.ts` already defines — swapping
 * `.main` for the `.dark` (light mode) / `.light` (dark mode) variant, rather than
 * inventing new colors, so each mode's dots stay legible against that mode's
 * `background.default` while still reading as "the same sphere, different theme."
 * Kept as a plain function (not a hook) so `ReactiveSphere` can call it directly from
 * inside the R3F tree without a second theme context dependency. */
export function getDotMeshColors(mode: ThemeMode): DotMeshThemeColors {
  return mode === 'dark'
    ? { idle: '#4C7B8B', reactive: '#D97650' } // primary.light / secondary.light
    : { idle: '#123340', reactive: '#7E2E12' } // primary.dark / secondary.dark
}
