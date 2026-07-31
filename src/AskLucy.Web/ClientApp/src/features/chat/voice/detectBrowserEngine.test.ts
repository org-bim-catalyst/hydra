import { describe, expect, it } from 'vitest'
import { detectBrowserEngine } from './detectBrowserEngine'

function fakeNavigator(userAgent: string, brands?: string[]): Navigator {
  return {
    userAgent,
    ...(brands ? { userAgentData: { brands: brands.map((brand) => ({ brand })) } } : {}),
  } as unknown as Navigator
}

describe('detectBrowserEngine', () => {
  it('detects Chromium via userAgentData brands', () => {
    expect(detectBrowserEngine(fakeNavigator('', ['Not:A-Brand', 'Chromium', 'Google Chrome']))).toBe('chromium')
  })

  it('detects Chromium via userAgentData brands for Edge', () => {
    expect(detectBrowserEngine(fakeNavigator('', ['Not:A-Brand', 'Chromium', 'Microsoft Edge']))).toBe('chromium')
  })

  it('detects Firefox via the user agent string when userAgentData is absent', () => {
    expect(
      detectBrowserEngine(
        fakeNavigator('Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0'),
      ),
    ).toBe('firefox')
  })

  it('detects Chromium via the user agent string when userAgentData is absent', () => {
    expect(
      detectBrowserEngine(
        fakeNavigator(
          'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        ),
      ),
    ).toBe('chromium')
  })

  it('detects WebKit/Safari on desktop via the user agent string', () => {
    expect(
      detectBrowserEngine(
        fakeNavigator(
          'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15',
        ),
      ),
    ).toBe('webkit')
  })

  it('detects WebKit/Safari on iOS via the user agent string', () => {
    expect(
      detectBrowserEngine(
        fakeNavigator(
          'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1',
        ),
      ),
    ).toBe('webkit')
  })

  it('returns unknown when neither signal yields a confident match', () => {
    expect(detectBrowserEngine(fakeNavigator(''))).toBe('unknown')
  })
})
