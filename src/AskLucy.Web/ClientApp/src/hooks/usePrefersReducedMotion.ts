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

/** Imperative, non-hook read of the same preference (`getSnapshot` above) — for the rare case
 * (specs/027-immersive-viewer-platform `viewerEngineStore`) where a value is needed once, outside
 * a React component, to compute a store's initial state. Prefer the hook everywhere else so
 * updates are observed reactively. */
export function prefersReducedMotion(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia(QUERY).matches
}
