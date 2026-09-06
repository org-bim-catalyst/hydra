import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../store/authStore'
import { apiFetch, ApiError } from './httpClient'

const server = setupServer()

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const assignSpy = vi.fn()

beforeEach(() => {
  useAuthStore.setState({ accessToken: 'expired-token', userId: 'user-1' })
  assignSpy.mockClear()
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { ...window.location, assign: assignSpy },
  })
})

describe('apiFetch — silent refresh on 401', () => {
  it('retries the original call once after a successful silent refresh', async () => {
    let protectedCallCount = 0
    server.use(
      http.get('*/api/v1/widgets', () => {
        protectedCallCount += 1
        return protectedCallCount === 1
          ? HttpResponse.json({ title: 'Authentication required' }, { status: 401 })
          : HttpResponse.json({ ok: true })
      }),
      http.post('*/api/v1/auth/refresh', () =>
        HttpResponse.json({ userId: 'user-1', accessToken: 'new-token', expiresAtUtc: null, requiresTwoFactor: false }),
      ),
    )

    const result = await apiFetch<{ ok: boolean }>('/widgets')

    expect(result).toEqual({ ok: true })
    expect(protectedCallCount).toBe(2)
    expect(useAuthStore.getState().accessToken).toBe('new-token')
    expect(assignSpy).not.toHaveBeenCalled()
  })

  it('clears the session and redirects to /login when refresh also fails', async () => {
    server.use(
      http.get('*/api/v1/widgets', () => HttpResponse.json({ title: 'Authentication required' }, { status: 401 })),
      http.post('*/api/v1/auth/refresh', () => HttpResponse.json({ title: 'No refresh token present' }, { status: 401 })),
    )

    await expect(apiFetch('/widgets')).rejects.toBeInstanceOf(ApiError)

    expect(useAuthStore.getState().accessToken).toBeNull()
    expect(assignSpy).toHaveBeenCalledWith('/login')
  })

  it('dedupes concurrent 401s into a single /auth/refresh call', async () => {
    let refreshCallCount = 0
    server.use(
      http.get('*/api/v1/widgets', () => HttpResponse.json({ title: 'Authentication required' }, { status: 401 })),
      http.post('*/api/v1/auth/refresh', () => {
        refreshCallCount += 1
        return HttpResponse.json({ title: 'No refresh token present' }, { status: 401 })
      }),
    )

    await Promise.all([
      apiFetch('/widgets').catch(() => undefined),
      apiFetch('/widgets').catch(() => undefined),
      apiFetch('/widgets').catch(() => undefined),
    ])

    expect(refreshCallCount).toBe(1)
  })

  it('does not attempt a refresh for an isAuthFlow call — the 401 is a normal outcome for the caller', async () => {
    let refreshCallCount = 0
    server.use(
      http.post('*/api/v1/auth/login', () => HttpResponse.json({ title: 'Invalid credentials' }, { status: 401 })),
      http.post('*/api/v1/auth/refresh', () => {
        refreshCallCount += 1
        return HttpResponse.json({ title: 'No refresh token present' }, { status: 401 })
      }),
    )

    await expect(apiFetch('/auth/login', { method: 'POST', isAuthFlow: true })).rejects.toBeInstanceOf(ApiError)

    expect(refreshCallCount).toBe(0)
    expect(assignSpy).not.toHaveBeenCalled()
  })
})
