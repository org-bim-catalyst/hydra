# Contract: Design Token Modules

No REST/HTTP contract exists for this feature (FR-012 — no API changes). The contract
that matters here is the **internal TypeScript interface** every feature/page consumes
from `src/theme/`. This is the surface every one of the ~15 in-scope pages depends on, so
a breaking change to it breaks every page at once — it is documented as a contract for
that reason.

## `createAppTheme(mode: ThemeMode): Theme`

Entry point, unchanged in signature. `ThemeMode` remains `'light' | 'dark'`, sourced from
the existing `store/themeStore`.

```ts
function createAppTheme(mode: ThemeMode): Theme
```

## Token module exports (extended, per data-model.md)

```ts
// palette.ts — existing, extended
export const radius: { xs: number; sm: number; md: number; lg: number; xl: number; pill: number }
export const opacity: { disabled: number; hover: number; overlay: number } // NEW
export function createPalette(mode: ThemeMode): PaletteOptions

// typography.ts — existing, unchanged
export const typography: TypographyVariantsOptions

// shadows.ts — existing, unchanged
export function createShadows(isDark: boolean): Shadows

// glass.ts — existing, unchanged shape; consumer scope generalized (research.md #3)
export interface GlassTokens {
  background: string
  backgroundElevated: string
  border: string
  backdropFilter: string
}
export function createGlassTokens(mode: ThemeMode): GlassTokens

// motion.ts — NEW
export interface MotionTokens {
  duration: { fast: number; standard: number; slow: number }
  easing: { standard: string; decelerate: string; accelerate: string }
}
export function createMotionTokens(prefersReducedMotion: boolean): MotionTokens

// zIndex.ts — NEW
export const zIndex: { appShell: number; dropdown: number; dialog: number; snackbar: number; tooltip: number }

// components.ts — existing, extended incrementally per page
export function createComponents(): Components<Theme>
```

## Consumer contract

Every feature component MUST obtain color/spacing/radius/shadow/motion/z-index values via
the MUI `theme` object (`useTheme()`, `sx` callback, or `styled()`), never via a hardcoded
literal — this is the enforceable half of the contract (constitution §7). A page that
hardcodes a hex color or a `setTimeout(200)` animation duration is a contract violation
even if it visually matches the design, because it cannot follow a future token change.

## Backward compatibility

`createPalette`, `createShadows`, `createGlassTokens`, and `createComponents` keep their
existing exported names and signatures — no consumer import needs to change. `motion.ts`
and `zIndex.ts` are additive new modules; existing code that does not import them is
unaffected.
