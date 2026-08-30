import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiProvider, AiCapabilityAssignment } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { CapabilityAssignmentsSection } from './CapabilityAssignmentsSection'

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getCapabilityAssignments: vi.fn(), setCapabilityAssignment: vi.fn() }
})

function makeProvider(overrides: Partial<AdminAiProvider>): AdminAiProvider {
  return {
    id: 'provider-openai',
    providerKey: 'openai',
    displayName: 'OpenAI',
    isEnabled: true,
    hasCredential: true,
    credentialLastRotatedAtUtc: null,
    defaultModelId: 'model-gpt41',
    healthStatus: 'Healthy',
    healthStatusCheckedAtUtc: null,
    healthFailureKind: null,
    healthFailureReason: null,
    healthStaleAfterUtc: null,
    isEffectivePlatformDefault: true,
    ...overrides,
  }
}

const openai = makeProvider({})
const anthropic = makeProvider({
  id: 'provider-anthropic',
  providerKey: 'anthropic',
  displayName: 'Anthropic',
  isEffectivePlatformDefault: false,
})

const unassigned: AiCapabilityAssignment = {
  capability: 'LocationIntent',
  providerId: null,
  effectiveProviderId: 'provider-openai',
  effectiveModelId: 'model-gpt41',
}

function renderSection(assignments: AiCapabilityAssignment[], providers: AdminAiProvider[] = [openai, anthropic]) {
  vi.mocked(adminAiProvidersApi.getCapabilityAssignments).mockResolvedValue(assignments)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <CapabilityAssignmentsSection providers={providers} />
    </QueryClientProvider>,
  )
}

beforeEach(() => vi.clearAllMocks())

/**
 * Options are queried by text inside the open listbox rather than by role. Testing Library's
 * role queries run an accessibility check that calls getComputedStyle, and jsdom throws
 * "object null is not iterable" from its font-size resolver on MUI's portalled menu — a jsdom
 * bug, not a problem with the component. The menu itself opens fine.
 */
async function openProviderMenu(capabilityLabel: string) {
  fireEvent.mouseDown(await screen.findByRole('combobox', { name: `Provider for ${capabilityLabel}` }))
  return within(document.querySelector('ul[role="listbox"]') as HTMLElement)
}

describe('CapabilityAssignmentsSection', () => {
  it('assigns a provider to a capability', async () => {
    vi.mocked(adminAiProvidersApi.setCapabilityAssignment).mockResolvedValue(undefined)
    renderSection([unassigned])

    const menu = await openProviderMenu('Location intent')
    fireEvent.click(menu.getByText('Anthropic'))

    await waitFor(() =>
      expect(adminAiProvidersApi.setCapabilityAssignment).toHaveBeenCalledWith('LocationIntent', 'provider-anthropic'),
    )
  })

  it('clears the assignment back to the platform default', async () => {
    vi.mocked(adminAiProvidersApi.setCapabilityAssignment).mockResolvedValue(undefined)
    renderSection([{ ...unassigned, providerId: 'provider-anthropic' }])

    const menu = await openProviderMenu('Location intent')
    fireEvent.click(menu.getByText('Platform default'))

    await waitFor(() =>
      expect(adminAiProvidersApi.setCapabilityAssignment).toHaveBeenCalledWith('LocationIntent', null),
    )
  })

  it('shows where an unassigned capability actually lands, not just that it is unassigned', async () => {
    // "Unassigned" and "not working" are different states. Conflating them is what let a
    // capability run on a provider nobody chose without anything on screen saying so.
    renderSection([unassigned])

    expect(await screen.findByText(/via platform default/)).toBeInTheDocument()
    expect(screen.getByText('OpenAI')).toBeInTheDocument()
  })

  it('reports plainly when nothing can serve a capability', async () => {
    renderSection([{ ...unassigned, effectiveProviderId: null, effectiveModelId: null }])

    expect(await screen.findByText('Nothing can serve this yet')).toBeInTheDocument()
  })

  it('lists every enabled, configured provider — not only the ones ready to be assigned', async () => {
    // Filtering out providers without a default model showed OpenAI alone while Anthropic and
    // Google Gemini sat enabled and configured, with nothing on screen explaining the absence.
    // They are listed, and the reason they cannot be picked yet is stated on the row.
    const noDefaultModel = makeProvider({
      id: 'provider-gemini',
      providerKey: 'google-gemini',
      displayName: 'Google Gemini',
      defaultModelId: null,
      isEffectivePlatformDefault: false,
    })
    renderSection([unassigned], [openai, noDefaultModel])

    const menu = await openProviderMenu('Location intent')

    expect(menu.getByText('OpenAI')).toBeInTheDocument()
    expect(menu.getByText('Google Gemini')).toBeInTheDocument()
    expect(menu.getByText(/set a default model first/)).toBeInTheDocument()
  })

  it('excludes a provider that is not enabled or has no credential', async () => {
    // OpenRouter's state on a fresh install. Offering it would store an assignment the resolver
    // must immediately fall back from.
    const openrouter = makeProvider({
      id: 'provider-openrouter',
      providerKey: 'openrouter',
      displayName: 'OpenRouter',
      isEnabled: false,
      hasCredential: false,
      defaultModelId: null,
      isEffectivePlatformDefault: false,
    })
    renderSection([unassigned], [openai, openrouter])

    const menu = await openProviderMenu('Location intent')

    expect(menu.queryByText('OpenRouter')).not.toBeInTheDocument()
  })

  it('cannot assign a provider that has no default model yet', async () => {
    const noDefaultModel = makeProvider({
      id: 'provider-gemini',
      displayName: 'Google Gemini',
      defaultModelId: null,
      isEffectivePlatformDefault: false,
    })
    renderSection([unassigned], [openai, noDefaultModel])

    const menu = await openProviderMenu('Location intent')
    fireEvent.click(menu.getByText('Google Gemini'))

    expect(adminAiProvidersApi.setCapabilityAssignment).not.toHaveBeenCalled()
  })

  it('surfaces a rejected assignment to the user rather than only the console', async () => {
    vi.mocked(adminAiProvidersApi.setCapabilityAssignment).mockRejectedValue(new Error('boom'))
    renderSection([unassigned])

    const menu = await openProviderMenu('Location intent')
    fireEvent.click(menu.getByText('Anthropic'))

    expect(await screen.findByText(/Something went wrong/)).toBeInTheDocument()
  })
})
