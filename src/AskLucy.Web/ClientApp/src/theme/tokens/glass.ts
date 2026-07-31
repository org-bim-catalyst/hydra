import type { ThemeMode } from '../../store/themeStore'

export interface GlassTokens {
  /** Translucent panel background — layers over the 3D scene, not a solid surface. */
  background: string
  /** Slightly stronger tint for hover/active sub-surfaces (menus, list rows) within the panel. */
  backgroundElevated: string
  border: string
  /** CSS `backdrop-filter` value; panel content stays legible over the moving scene (SC-004). */
  backdropFilter: string
}

/** Glassmorphism tokens for the floating assistant panel (FR-005). Derived from the
 * existing graphite/vellum palette rather than a generic white/black glass, so the
 * panel reads as part of this theme rather than a bolted-on effect. */
export function createGlassTokens(mode: ThemeMode): GlassTokens {
  const isDark = mode === 'dark'

  return {
    background: isDark ? 'rgba(29, 27, 23, 0.72)' : 'rgba(255, 255, 255, 0.72)',
    backgroundElevated: isDark ? 'rgba(38, 36, 31, 0.85)' : 'rgba(247, 246, 242, 0.85)',
    border: isDark ? 'rgba(247, 246, 242, 0.12)' : 'rgba(23, 22, 19, 0.08)',
    backdropFilter: 'blur(20px) saturate(150%)',
  }
}
