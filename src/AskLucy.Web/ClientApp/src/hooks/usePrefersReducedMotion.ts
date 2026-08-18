import { useSyncExternalStore } from 'react'

const QUERY = '(prefers-reduced-motion: reduce)'

function subscribe(callback: () => void) {
  const mediaQueryList = window.matchMedia(QUERY)
  mediaQueryList.addEventListener('change', callback)
  return () => mediaQueryList.removeEventListener('change', callback)
}

function getSnapshot() {
  return window.matchMedia(QUERY).matches
}

/** Single source of truth for the OS/browser "reduce motion" preference (FR-010).
 * Consumed by `theme/index.ts` (so MUI's own Dialog/Drawer/Menu/Collapse transitions
 * inherit it) and directly by the particle-sphere scene, whose motion is driven by
 * `requestAnimationFrame` rather than CSS and so cannot be gated by a media query alone. */
export function usePrefersReducedMotion(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, () => false)
}
