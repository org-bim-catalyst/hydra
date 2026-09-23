import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { modelsInEveryState } from './customModelFixtures'
import { CustomModelsSection } from './CustomModelsSection'

const hub = vi.hoisted(() => ({
  state: { isLive: true, connectionLost: false, phaseById: {} as Record<string, string> },
}))
vi.mock('../../hooks/useCustomModelDeploymentsHub', () => ({
  useCustomModelDeploymentsHub: () => hub.state,
}))

expect.extend(toHaveNoViolations)

function listOf(items: CustomModelSummary[]) {
  return http.get('*/api/v1/admin/custom-models', () =>
    HttpResponse.json({ items, page: 1, pageSize: 50, totalCount: items.length }),
  )
}

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: ['admin.custom-models.view', 'admin.custom-models.manage'],
    }),
  ),
  listOf([]),
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({ isConfigured: true, transport: 'FTPS', maxDeploymentBytes: 2 ** 30, allowedDestinationPrefixes: ['Models'] }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderSection() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <CustomModelsSection />
    </QueryClientProvider>,
  )
}

describe('CustomModelsSection accessibility', () => {
  it('has no automatically detectable a11y violations in the empty state (constitution §10)', async () => {
    const { container, findByText } = renderSection()

    await findByText('No custom models yet.')
    await findByText('Add model')

    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with a row in every deployment state (constitution §10)', async () => {
    server.use(listOf(modelsInEveryState))
    const { container, findByText } = renderSection()

    await findByText('model-cancelled')

    expect(await axe(container)).toHaveNoViolations()
  })
})
