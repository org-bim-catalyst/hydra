import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { configureAxe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { LocationWeatherWidget } from './LocationWeatherWidget'

expect.extend(toHaveNoViolations)

const axe = configureAxe({ rules: { region: { enabled: false } } })

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  useActiveLocationStore.getState().clear()
})
afterAll(() => server.close())

function renderWidget() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <LocationWeatherWidget />
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

describe('LocationWeatherWidget accessibility (T022)', () => {
  it('has no axe violations in the null/loading state (no location set)', async () => {
    const { container } = renderWidget()
    // Widget renders nothing — no DOM nodes to violate a11y rules.
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no axe violations in the populated state (location name + temperature)', async () => {
    server.use(http.get('*/api/v1/weather/current', () => HttpResponse.json(snapshot)))
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    const { container, findByRole } = renderWidget()

    // Wait for data to load before running axe.
    await findByRole('status')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no axe violations in the stale state ("Last known reading" badge)', async () => {
    let callCount = 0
    server.use(
      http.get('*/api/v1/weather/current', () => {
        callCount += 1
        return callCount === 1 ? HttpResponse.json(snapshot) : new HttpResponse(null, { status: 502 })
      }),
    )
    useActiveLocationStore.getState().setFromGeolocation(51.5074, -0.1278)
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget />
      </QueryClientProvider>,
    )
    await findByRole('status')
    await queryClient.refetchQueries({ queryKey: ['weather', 'current', 51.5074, -0.1278] })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
