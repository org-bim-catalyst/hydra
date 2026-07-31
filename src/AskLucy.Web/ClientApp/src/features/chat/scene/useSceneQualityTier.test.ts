import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useSceneQualityTier } from './useSceneQualityTier'

type Listener = () => void

const MOBILE_QUERY = '(max-width: 599.95px)'
const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)'

function installMatchMedia(initial: Record<string, boolean>) {
  const listeners = new Map<string, Set<Listener>>()
  const state = { ...initial }

  window.matchMedia = vi.fn((query: string) => {
    return {
      get matches() {
        return state[query] ?? false
      },
      media: query,
      addEventListener: (_type: string, listener: Listener) => {
        if (!listeners.has(query)) listeners.set(query, new Set())
        listeners.get(query)!.add(listener)
      },
      removeEventListener: (_type: string, listener: Listener) => {
        listeners.get(query)?.delete(listener)
      },
    } as unknown as MediaQueryList
  }) as unknown as typeof window.matchMedia

  return {
    setMatches(query: string, matches: boolean) {
      state[query] = matches
      listeners.get(query)?.forEach((listener) => listener())
    },
  }
}

function installWebGL2(supported: boolean) {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(
    (contextId: string): RenderingContext | null =>
      contextId === 'webgl2' && supported ? ({} as WebGL2RenderingContext) : null,
  )
}

describe('useSceneQualityTier', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('reports static-fallback when WebGL2 is unsupported, regardless of viewport (FR-011)', () => {
    installWebGL2(false)
    installMatchMedia({ [MOBILE_QUERY]: false, [REDUCED_MOTION_QUERY]: false })

    const { result } = renderHook(() => useSceneQualityTier())

    expect(result.current.tier).toBe('static-fallback')
  })

  it('reports full on a wide viewport with WebGL2 support', () => {
    installWebGL2(true)
    installMatchMedia({ [MOBILE_QUERY]: false, [REDUCED_MOTION_QUERY]: false })

    const { result } = renderHook(() => useSceneQualityTier())

    expect(result.current.tier).toBe('full')
  })

  it('reports reduced on a narrow viewport with WebGL2 support (FR-010)', () => {
    installWebGL2(true)
    installMatchMedia({ [MOBILE_QUERY]: true, [REDUCED_MOTION_QUERY]: false })

    const { result } = renderHook(() => useSceneQualityTier())

    expect(result.current.tier).toBe('reduced')
  })

  it('reflects prefers-reduced-motion (FR-012)', () => {
    installWebGL2(true)
    installMatchMedia({ [MOBILE_QUERY]: false, [REDUCED_MOTION_QUERY]: true })

    const { result } = renderHook(() => useSceneQualityTier())

    expect(result.current.prefersReducedMotion).toBe(true)
  })

  it('reportPerformanceRegression steps full down to reduced, and stays there (research.md §4)', () => {
    installWebGL2(true)
    installMatchMedia({ [MOBILE_QUERY]: false, [REDUCED_MOTION_QUERY]: false })

    const { result } = renderHook(() => useSceneQualityTier())
    expect(result.current.tier).toBe('full')

    act(() => result.current.reportPerformanceRegression())
    expect(result.current.tier).toBe('reduced')

    act(() => result.current.reportPerformanceRegression())
    expect(result.current.tier).toBe('reduced')
  })

  it('reportPerformanceRegression never upgrades out of static-fallback', () => {
    installWebGL2(false)
    installMatchMedia({ [MOBILE_QUERY]: false, [REDUCED_MOTION_QUERY]: false })

    const { result } = renderHook(() => useSceneQualityTier())
    act(() => result.current.reportPerformanceRegression())

    expect(result.current.tier).toBe('static-fallback')
  })
})
