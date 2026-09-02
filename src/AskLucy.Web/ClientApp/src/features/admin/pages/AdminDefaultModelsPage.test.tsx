import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AdminDefaultModelsPage } from './AdminDefaultModelsPage'

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getProviders: vi.fn(), getModels: vi.fn(), updateProvider: vi.fn() }
})

const openai: AdminAiProvider = {
  id: 'provider-openai',
  providerKey: 'openai',
  displayName: 'OpenAI',
  isEnabled: true,
  hasCredential: true,
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: null,
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: null,
}

const model = (overrides: Partial<AdminAiModel>): AdminAiModel => ({
  id: 'model-gpt41',
  modelKey: 'gpt-4.1',
  displayName: 'GPT-4.1',
  contextWindowTokens: null,
  maxOutputTokens: null,
  capabilities: {
    streaming: true,
    vision: false,
    functionCalling: false,
    jsonMode: false,
    reasoning: false,
    embeddings: false,
    imageInput: false,
    imageOutput: false,
    audio: false,
  },
  pricing: null,
  releaseDate: null,
  status: 'Available',
  ...overrides,
})

function renderPage(models: AdminAiModel[], providers: AdminAiProvider[] = [openai]) {
  vi.mocked(adminAiProvidersApi.getProviders).mockResolvedValue(providers)
  vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue(models)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminDefaultModelsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * Deliberately plain DOM queries, no `getByRole`. Testing Library's role queries run an
 * accessibility check that calls getComputedStyle, and jsdom throws "object null is not
 * iterable" out of its font-size resolver once MUI's portalled menu is in the tree — a jsdom
 * bug, not a component problem. The accessible name is still asserted, in its own test below.
 */
function comboboxFor(providerId: string) {
  return document.querySelector<HTMLElement>(
    `[role="combobox"][aria-labelledby="${providerId}-default-model-label"]`,
  )
}

async function openModelMenu(providerId: string) {
  // The Select stays disabled until that provider's model query resolves. Opening it earlier
  // silently does nothing, and the test then fails on a missing listbox rather than on the
  // behaviour under test.
  await waitFor(() => {
    const combobox = comboboxFor(providerId)
    // Both assertions matter: `?.` on a missing element yields undefined, which satisfies
    // "not disabled" and lets the wait pass before the row has even rendered.
    expect(combobox).not.toBeNull()
    expect(combobox?.getAttribute('aria-disabled')).not.toBe('true')
  })
  fireEvent.mouseDown(comboboxFor(providerId) as HTMLElement)
  // MUI opens the menu through a Popover transition, so the listbox is not in the DOM on the
  // same tick as the mouseDown.
  await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
  return within(document.querySelector('ul[role="listbox"]') as HTMLElement)
}

beforeEach(() => vi.clearAllMocks())

describe('AdminDefaultModelsPage', () => {
  it('sets a provider default from its Available models', async () => {
    vi.mocked(adminAiProvidersApi.updateProvider).mockResolvedValue(undefined)
    renderPage([model({})])

    const menu = await openModelMenu('provider-openai')
    fireEvent.click(menu.getByText('GPT-4.1'))

    await waitFor(() =>
      expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-openai', {
        defaultModelId: 'model-gpt41',
      }),
    )
  })

  it('offers only Available models', async () => {
    // DefaultProviderResolver requires IsSelectable, so a Deprecated default would be skipped at
    // runtime and every capability on this provider would quietly fall back elsewhere.
    renderPage([model({}), model({ id: 'model-old', displayName: 'GPT-4 legacy', status: 'Deprecated' })])

    const menu = await openModelMenu('provider-openai')

    expect(menu.getByText('GPT-4.1')).toBeInTheDocument()
    expect(menu.queryByText('GPT-4 legacy')).not.toBeInTheDocument()
  })

  it('clears via the explicit flag, never by sending a null id', async () => {
    // Server-side a null defaultModelId means "leave it alone", so that a PATCH toggling
    // isEnabled cannot wipe the default as a side effect. Clearing needs its own signal.
    vi.mocked(adminAiProvidersApi.updateProvider).mockResolvedValue(undefined)
    renderPage([model({})], [{ ...openai, defaultModelId: 'model-gpt41' }])

    const menu = await openModelMenu('provider-openai')
    fireEvent.click(menu.getByText('No default'))

    await waitFor(() =>
      expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-openai', {
        clearDefaultModel: true,
      }),
    )
  })

  it('explains an empty model list instead of showing a dead control', async () => {
    renderPage([model({ status: 'Unavailable' })])

    expect(await screen.findByText(/No Available models/)).toBeInTheDocument()
  })

  it('names each control for a screen reader', async () => {
    renderPage([model({})])

    await waitFor(() => expect(comboboxFor('provider-openai')).not.toBeNull())
    const labelId = comboboxFor('provider-openai')?.getAttribute('aria-labelledby')
    expect(document.getElementById(labelId ?? '')?.textContent).toBe('Default model for OpenAI')
  })

  it('surfaces a failed save to the user rather than only the console', async () => {
    vi.mocked(adminAiProvidersApi.updateProvider).mockRejectedValue(new Error('boom'))
    renderPage([model({})])

    const menu = await openModelMenu('provider-openai')
    fireEvent.click(menu.getByText('GPT-4.1'))

    expect(await screen.findByText(/Something went wrong/)).toBeInTheDocument()
  })
})
