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

  it('offers only providers that are enabled and have a default model', async () => {
    // Assigning anything else would store a setting that silently does nothing — the resolver
    // would log it as unusable and fall straight back to the platform default.
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
    expect(menu.queryByText('Google Gemini')).not.toBeInTheDocument()
  })

  it('surfaces a rejected assignment to the user rather than only the console', async () => {
    vi.mocked(adminAiProvidersApi.setCapabilityAssignment).mockRejectedValue(new Error('boom'))
    renderSection([unassigned])

    const menu = await openProviderMenu('Location intent')
    fireEvent.click(menu.getByText('Anthropic'))

    expect(await screen.findByText(/Something went wrong/)).toBeInTheDocument()
  })
})
