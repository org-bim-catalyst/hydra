import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { LocationWeatherWidget } from './LocationWeatherWidget'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/weather/current', () =>
    HttpResponse.json({
      locationName: 'London, United Kingdom',
      temperatureCelsius: 15.4,
      condition: 'Cloudy',
      isDaytime: true,
      observedAtUtc: new Date().toISOString(),
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('LocationWeatherWidget accessibility (FR-009)', () => {
  it('has no automatically detectable a11y violations once loaded', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <LocationWeatherWidget latitude={51.5074} longitude={-0.1278} />
      </QueryClientProvider>,
    )

    await findByRole('status')
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
