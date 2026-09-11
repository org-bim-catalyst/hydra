import { afterEach, describe, expect, it } from 'vitest'
import { BUILDING_FOOTPRINT_FILL_COLOR, BUILDING_FOOTPRINT_STROKE_COLOR } from './buildingFootprintColors'
import { BUILDINGS_ONLY_STYLE, shouldReduceMapQuality } from './GoogleMapsGisLayer'

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

// specs/048-buildings-only-map-style: `setMapTypeId`'s live map.setOptions() behavior lives
// inside `createGoogleMapsGisLayer`'s closure — not unit-testable here for the same reason
// `shouldReduceMapQuality` is this file's only other coverage (see the file-level comment
// above). The style content itself is pure data and worth locking down directly; the full
// apply/clear behavior is covered by quickstart.md's manual/E2E validation.
describe('BUILDINGS_ONLY_STYLE (specs/048-buildings-only-map-style FR-002)', () => {
  it('hides exactly the five categories that compete with building footprints', () => {
    const hidingRules = BUILDINGS_ONLY_STYLE.filter((rule) => rule.featureType !== 'landscape.man_made')
    expect(hidingRules.map((rule) => rule.featureType)).toEqual([
      'road',
      'poi',
      'transit',
      'administrative',
      'landscape.natural',
    ])
    for (const rule of hidingRules) {
      expect(rule.stylers).toEqual([{ visibility: 'off' }])
    }
  })

  it('colors building footprints with the fixed, deterministic color-key values', () => {
    const fillRule = BUILDINGS_ONLY_STYLE.find(
      (rule) => rule.featureType === 'landscape.man_made' && rule.elementType === 'geometry.fill',
    )
    const strokeRule = BUILDINGS_ONLY_STYLE.find(
      (rule) => rule.featureType === 'landscape.man_made' && rule.elementType === 'geometry.stroke',
    )
    expect(fillRule?.stylers).toEqual([{ color: BUILDING_FOOTPRINT_FILL_COLOR }])
    expect(strokeRule?.stylers).toEqual([{ color: BUILDING_FOOTPRINT_STROKE_COLOR }])
  })
})
