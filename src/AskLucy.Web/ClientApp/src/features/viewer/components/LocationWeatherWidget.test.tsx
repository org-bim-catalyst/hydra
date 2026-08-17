import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { LocationWeatherWidget } from './LocationWeatherWidget'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderWidget(latitude: number | null, longitude: number | null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <LocationWeatherWidget latitude={latitude} longitude={longitude} />
    </QueryClientProvider>,
  )
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
    renderWidget(null, null)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the location name, temperature, and a condition-appropriate readout once loaded', async () => {
    server.use(http.get('*/api/v1/weather/current', () => HttpResponse.json(snapshot)))
    renderWidget(51.5074, -0.1278)

    const widget = await screen.findByRole('status')
    expect(widget).toHaveTextContent('London, United Kingdom')
    expect(widget).toHaveTextContent('15°C')
    expect(widget).not.toHaveTextContent('Last known reading')
  })

  it('renders nothing on a persistent failure with no prior successful reading (FR-011)', async () => {
    server.use(http.get('*/api/v1/weather/current', () => new HttpResponse(null, { status: 502 })))
    renderWidget(51.5074, -0.1278)

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
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget latitude={51.5074} longitude={-0.1278} />
      </QueryClientProvider>,
    )
    await screen.findByRole('status')

    // Force a refetch (simulating the periodic refresh interval firing) that fails.
    await queryClient.refetchQueries({ queryKey: ['weather', 'current', 51.5074, -0.1278] })
    rerender(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget latitude={51.5074} longitude={-0.1278} />
      </QueryClientProvider>,
    )

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
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget latitude={51.5074} longitude={-0.1278} />
      </QueryClientProvider>,
    )
    await screen.findByRole('status')
    const requestsWhileResolved = requestCount

    // Mirrors ViewerSurface's own reaction to useGeolocation going from 'granted' to
    // 'unavailable' — this component only ever sees that as latitude/longitude turning null.
    rerender(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget latitude={null} longitude={null} />
      </QueryClientProvider>,
    )

    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    // No further requests fire for the now-disabled query even if time passes.
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(requestCount).toBe(requestsWhileResolved)
  })
})
