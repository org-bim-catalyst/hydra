import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../api/httpClient'
import * as adminHangfireApi from '../api/adminHangfireApi'
import { useOpenHangfireDashboard } from './useOpenHangfireDashboard'

vi.mock('../api/adminHangfireApi', () => ({
  postHangfireSession: vi.fn(),
}))

const postHangfireSessionMock = vi.mocked(adminHangfireApi.postHangfireSession)

describe('useOpenHangfireDashboard', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('pre-opens a blank tab before awaiting the mint call, then navigates it to /hangfire on success', async () => {
    const fakeTab = { location: { href: '' }, close: vi.fn() }
    let openedBeforeMintResolved = false
    postHangfireSessionMock.mockImplementation(
      () =>
        new Promise((resolve) => {
          openedBeforeMintResolved = windowOpenSpy.mock.calls.length === 1
          setTimeout(() => resolve(undefined), 0)
        }),
    )
    const windowOpenSpy = vi.spyOn(window, 'open').mockReturnValue(fakeTab as unknown as Window)

    const { result } = renderHook(() => useOpenHangfireDashboard())

    await act(async () => {
      await result.current.open()
    })

    expect(windowOpenSpy).toHaveBeenCalledWith('', '_blank')
    expect(openedBeforeMintResolved).toBe(true)
    expect(fakeTab.location.href).toBe('/hangfire')
    expect(result.current.errorMessage).toBeNull()
  })

  it('shows a visible error and closes the blank tab when the mint call fails', async () => {
    const fakeTab = { location: { href: '' }, close: vi.fn() }
    vi.spyOn(window, 'open').mockReturnValue(fakeTab as unknown as Window)
    postHangfireSessionMock.mockRejectedValue(new ApiError(500, 'Request failed', 'Something went wrong.'))

    const { result } = renderHook(() => useOpenHangfireDashboard())

    await act(async () => {
      await result.current.open()
    })

    expect(fakeTab.close).toHaveBeenCalled()
    expect(fakeTab.location.href).toBe('')
    await waitFor(() => expect(result.current.errorMessage).toBe('Something went wrong.'))
  })

  it('shows a visible pop-up-blocked message and never calls the mint endpoint when window.open returns null', async () => {
    vi.spyOn(window, 'open').mockReturnValue(null)

    const { result } = renderHook(() => useOpenHangfireDashboard())

    await act(async () => {
      await result.current.open()
    })

    expect(postHangfireSessionMock).not.toHaveBeenCalled()
    expect(result.current.errorMessage).toMatch(/pop-up/i)
  })
})
