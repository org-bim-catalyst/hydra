import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { NotificationPreferences } from '../../notifications/api/notificationPreferencesApi'
import { NotificationPreferencesTab } from './NotificationPreferencesTab'

expect.extend(toHaveNoViolations)

const preferences: NotificationPreferences = {
  categories: [
    {
      category: 'Security',
      channels: [
        { channel: 'InApp', enabled: true, locked: true },
        { channel: 'Email', enabled: true, locked: true },
      ],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
    {
      category: 'Memory',
      channels: [{ channel: 'InApp', enabled: true, locked: false }],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
  ],
}

const server = setupServer(http.get('*/api/v1/users/me/notification-preferences', () => HttpResponse.json(preferences)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('NotificationPreferencesTab accessibility (T137)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByRole, container } = render(
      <QueryClientProvider client={queryClient}>
        <NotificationPreferencesTab />
      </QueryClientProvider>,
    )
    await findByRole('switch', { name: 'Memory notifications by In-app' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
