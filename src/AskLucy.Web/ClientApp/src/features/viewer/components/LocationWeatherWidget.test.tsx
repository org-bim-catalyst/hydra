import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { LocationWeatherWidget } from './LocationWeatherWidget'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  useActiveLocationStore.getState().clear()
})
afterAll(() => server.close())

// specs/036-startup-geolocation T007/T008: widget reads from the store, not props.
// Helpers populate the store before render to simulate geolocation or agent-sourced locations.
function renderWidget() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return {
    queryClient,
    ...render(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget />
      </QueryClientProvider>,
    ),
  }
}

const snapshot = {
  locationName: 'London, United Kingdom',
  temperatureCelsius: 15.4,
  condition: 'Cloudy',
  isDaytime: true,
  observedAtUtc: new Date().toISOString(),
}

describe('LocationWeatherWidget (US4, FR-009/FR-010/FR-011)', () => {
  it('renders nothing while location has not resolved (FR-008)', () => {
    // Store starts empty (afterEach clears it)
    renderWidget()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the location name, temperature, and a condition-appropriate readout once loaded', async () => {
    server.use(http.get('*/api/v1/weather/current', () => HttpResponse.json(snapshot)))
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderWidget()

    const widget = await screen.findByRole('status')
    expect(widget).toHaveTextContent('London, United Kingdom')
    expect(widget).toHaveTextContent('15°C')
    expect(widget).not.toHaveTextContent('Last known reading')
  })

  it('renders nothing on a persistent failure with no prior successful reading (FR-011)', async () => {
    server.use(http.get('*/api/v1/weather/current', () => new HttpResponse(null, { status: 502 })))
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderWidget()

    await waitFor(() => expect(screen.queryByRole('status')).not.toBeInTheDocument())
  })

  it('shows a clearly marked stale reading rather than going blank when a later refresh fails (FR-011)', async () => {
    let callCount = 0
    server.use(
      http.get('*/api/v1/weather/current', () => {
        callCount += 1
        return callCount === 1 ? HttpResponse.json(snapshot) : new HttpResponse(null, { status: 502 })
      }),
    )
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    const { queryClient } = renderWidget()
    await screen.findByRole('status')

    // Force a refetch that fails — simulates the user's weather data going stale.
    await act(async () => {
      await queryClient.refetchQueries({ queryKey: ['weather', 'current', 51.5074, -0.1278] })
    })

    const widget = await screen.findByRole('status')
    expect(widget).toHaveTextContent('London, United Kingdom') // last-known reading retained
    expect(widget).toHaveTextContent('Last known reading')
  })

  it('disappears and stops fetching once location becomes unavailable mid-session (FR-012)', async () => {
    let requestCount = 0
    server.use(
      http.get('*/api/v1/weather/current', () => {
        requestCount += 1
        return HttpResponse.json(snapshot)
      }),
    )
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderWidget()
    await screen.findByRole('status')
    const requestsWhileResolved = requestCount

    // Mirrors ChatPage clearing the store when geolocation goes unavailable (FR-012).
    act(() => {
      useActiveLocationStore.getState().clear()
    })

    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    // No further requests fire for the now-disabled query even if time passes.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(requestCount).toBe(requestsWhileResolved)
  })

  // T008 extension (a): weather success → locationName written back into the store (FR-008/SC-005)
  it('writes the resolved locationName back into activeLocationStore when weather data loads', async () => {
    server.use(http.get('*/api/v1/weather/current', () => HttpResponse.json(snapshot)))
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderWidget()

    await screen.findByRole('status')

    expect(useActiveLocationStore.getState().locationName).toBe('London, United Kingdom')
  })

  // T008 extension (b): SC-005 — when weather API returns empty locationName, store gets "${lat}, ${lon}"
  it('falls back to coordinate string in the store when the weather API returns no location name (SC-005)', async () => {
    server.use(
      http.get('*/api/v1/weather/current', () =>
        HttpResponse.json({ ...snapshot, locationName: '' }),
      ),
    )
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    renderWidget()

    await screen.findByRole('status')

    expect(useActiveLocationStore.getState().locationName).toBe('51.5074, -0.1278')
  })

  // T008 extension (c): weather failure after first success → stale badge shown, prior locationName retained
  it('retains the previous locationName in the store after a weather refresh failure', async () => {
    let callCount = 0
    server.use(
      http.get('*/api/v1/weather/current', () => {
        callCount += 1
        return callCount === 1 ? HttpResponse.json(snapshot) : new HttpResponse(null, { status: 502 })
      }),
    )
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    const { queryClient } = renderWidget()
    await screen.findByRole('status')
    expect(useActiveLocationStore.getState().locationName).toBe('London, United Kingdom')

    await act(async () => {
      await queryClient.refetchQueries({ queryKey: ['weather', 'current', 51.5074, -0.1278] })
    })

    // Widget shows stale badge; store locationName is unchanged (the useEffect only runs on
    // successful data, not on error, so the coordinate guard never had a chance to clear it).
    expect(screen.getByRole('status')).toHaveTextContent('Last known reading')
    expect(useActiveLocationStore.getState().locationName).toBe('London, United Kingdom')
  })
})
