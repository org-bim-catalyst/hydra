import { afterEach, describe, expect, it } from 'vitest'
import { shouldReduceMapQuality } from './GoogleMapsGisLayer'

// research.md Decision 10: the real `createGoogleMapsGisLayer` (Google Maps JS bootstrap +
// WebGLOverlayView/Three.js bridging) is not unit-testable in jsdom — no real Maps JS runtime
// or WebGL context is available. Only the pure device-capability logic is covered here; the
// actual map rendering is verified via the Playwright E2E spec and manual quickstart.md
// validation against a real browser + API key.
describe('shouldReduceMapQuality (T032a, FR-005a/SC-004a)', () => {
  const originalMatchMedia = window.matchMedia

  afterEach(() => {
    window.matchMedia = originalMatchMedia
  })

  it('returns true under the mobile breakpoint', () => {
    window.matchMedia = ((query: string) => ({
      matches: query.includes('max-width'),
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })) as typeof window.matchMedia

    expect(shouldReduceMapQuality()).toBe(true)
  })

  it('returns false above the mobile breakpoint', () => {
    window.matchMedia = ((query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })) as typeof window.matchMedia

    expect(shouldReduceMapQuality()).toBe(false)
  })
})
