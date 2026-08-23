import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useGeolocation } from './useGeolocation'

// Each test calls stubGeolocation() itself before rendering, so there's no need to restore
// the original (jsdom has no navigator.geolocation at all) between tests — doing so via
// afterEach would race @testing-library/react's own automatic unmount cleanup, which needs
// a well-formed stub (with clearWatch) still in place when the hook's effect cleanup runs.
function stubGeolocation(geolocation: Partial<Geolocation> | undefined) {
  Object.defineProperty(navigator, 'geolocation', { value: geolocation, configurable: true })
}

afterEach(() => {
  vi.useRealTimers()
})

describe('useGeolocation (FR-001/FR-013)', () => {
  it('starts resolving, then reports granted with the resolved coordinates (watchPosition path)', async () => {
    stubGeolocation({
      getCurrentPosition: vi.fn((_success, error) => {
        // Simulate high-accuracy attempt timing out — error callback fires
        error?.({ code: 3, message: 'Timeout' } as GeolocationPositionError)
      }),
      watchPosition: vi.fn((success) => {
        success({
          coords: { latitude: 51.5074, longitude: -0.1278 },
        } as GeolocationPosition)
        return 1
      }),
      clearWatch: vi.fn(),
    })

    const { result } = renderHook(() => useGeolocation())

    await waitFor(() => expect(result.current.status).toBe('granted'))
    expect(result.current.latitude).toBe(51.5074)
    expect(result.current.longitude).toBe(-0.1278)
  })

  it('reports granted immediately via high-accuracy getCurrentPosition when GPS is available (FR-013)', async () => {
    stubGeolocation({
      getCurrentPosition: vi.fn((success) => {
        success({
          coords: { latitude: 25.2048, longitude: 55.2708 },
        } as GeolocationPosition)
      }),
      watchPosition: vi.fn(() => 1),
      clearWatch: vi.fn(),
    })

    const { result } = renderHook(() => useGeolocation())

    await waitFor(() => expect(result.current.status).toBe('granted'))
    expect(result.current.latitude).toBe(25.2048)
    expect(result.current.longitude).toBe(55.2708)
  })

  it('reports unavailable when permission is denied, without throwing', async () => {
    stubGeolocation({
      getCurrentPosition: vi.fn((_success, error) => {
        error?.({ code: 1, message: 'User denied Geolocation' } as GeolocationPositionError)
      }),
      watchPosition: vi.fn((_success, error) => {
        error?.({ code: 1, message: 'User denied Geolocation' } as GeolocationPositionError)
        return 1
      }),
      clearWatch: vi.fn(),
    })

    const { result } = renderHook(() => useGeolocation())

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.latitude).toBeNull()
  })

  it('reports unavailable immediately when the browser has no geolocation API at all', () => {
    stubGeolocation(undefined)

    const { result } = renderHook(() => useGeolocation())

    expect(result.current.status).toBe('unavailable')
  })

  it('reports unavailable if a previously granted watch later errors (FR-012 — revoked mid-session)', async () => {
    let errorCallback: ((error: GeolocationPositionError) => void) | undefined
    stubGeolocation({
      getCurrentPosition: vi.fn((_success, error) => {
        error?.({ code: 3, message: 'Timeout' } as GeolocationPositionError)
      }),
      watchPosition: vi.fn((success, error) => {
        errorCallback = error ?? undefined
        success({ coords: { latitude: 1, longitude: 2 } } as GeolocationPosition)
        return 1
      }),
      clearWatch: vi.fn(),
    })

    const { result } = renderHook(() => useGeolocation())
    await waitFor(() => expect(result.current.status).toBe('granted'))

    act(() => {
      errorCallback?.({ code: 1, message: 'Permission revoked' } as GeolocationPositionError)
    })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
    expect(result.current.latitude).toBeNull()
  })

  it('transitions to unavailable after the 15 s watchPosition timeout with no fix (FR-005)', async () => {
    vi.useFakeTimers()

    stubGeolocation({
      getCurrentPosition: vi.fn((_success, error) => {
        // High-accuracy times out immediately
        error?.({ code: 3, message: 'Timeout' } as GeolocationPositionError)
      }),
      watchPosition: vi.fn((_success, error) => {
        // Low-accuracy times out after 15 s — simulated by advancing timers
        setTimeout(() => {
          error?.({ code: 3, message: 'Timeout' } as GeolocationPositionError)
        }, 15_000)
        return 1
      }),
      clearWatch: vi.fn(),
    })

    const { result } = renderHook(() => useGeolocation())
    expect(result.current.status).toBe('resolving')

    await act(async () => {
      vi.advanceTimersByTime(15_000)
    })

    await waitFor(() => expect(result.current.status).toBe('unavailable'))
  })

  it('clears the watch on unmount', () => {
    const clearWatch = vi.fn()
    stubGeolocation({
      getCurrentPosition: vi.fn((_success, error) => {
        error?.({ code: 3, message: 'Timeout' } as GeolocationPositionError)
      }),
      watchPosition: vi.fn(() => 42),
      clearWatch,
    })

    const { unmount } = renderHook(() => useGeolocation())
    unmount()

    expect(clearWatch).toHaveBeenCalledWith(42)
  })
})
