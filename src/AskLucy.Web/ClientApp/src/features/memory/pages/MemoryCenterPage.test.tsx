import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { MemoryCenterPage } from './MemoryCenterPage'
import type { MemoryDetail } from '../api/memoryApi'

const MEMORY_ID = '11111111-1111-1111-1111-111111111111'

const memoryDetail: MemoryDetail = {
  id: MEMORY_ID,
  category: 'UserPreference',
  content: 'Prefers dark mode.',
  state: 'Active',
  isSensitive: false,
  projectId: null,
  importance: 0.5,
  confidence: 0.9,
  history: [],
  openConflict: null,
}

const server = setupServer(
  http.get('*/api/v1/memories', () => HttpResponse.json({ results: [], nextCursor: null, totalCount: 0 })),
  http.get(`*/api/v1/memories/${MEMORY_ID}`, () => HttpResponse.json(memoryDetail)),
)

// Unmocked requests (memory-center notification/approval-queue polling, etc.) are bypassed
// rather than asserted on here: this suite only verifies the deep link opens the right dialog.
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAtPath(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <MemoryCenterPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('MemoryCenterPage ?memoryId= deep link (specs/067-notifications-communication-hub T092)', () => {
  it('opens the memory edit dialog for a valid ?memoryId=', async () => {
    renderAtPath(`/memory?memoryId=${MEMORY_ID}`)

    // jsdom's getComputedStyle throws once a MUI Dialog portal is open, so getByRole here is
    // avoided in favor of getByText (see chatpage/settings-dialog gotchas elsewhere in this repo).
    await screen.findByText('Edit memory')
    // The label's accessible text includes the MUI-added required-field asterisk ("Content *").
    expect(await screen.findByLabelText(/Content/)).toHaveValue('Prefers dark mode.')
  })

  it('shows an inline message for an unknown/inaccessible ?memoryId=, without crashing', async () => {
    server.use(
      http.get('*/api/v1/memories/22222222-2222-2222-2222-222222222222', () =>
        HttpResponse.json({ title: 'Not found', status: 404 }, { status: 404 }),
      ),
    )

    renderAtPath('/memory?memoryId=22222222-2222-2222-2222-222222222222')

    expect(await screen.findByText('This memory is no longer available.')).toBeInTheDocument()
  })
})
