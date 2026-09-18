import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiModel, AdminAiProvider, AiCapabilityAssignment } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { CapabilityAssignmentsSection } from './CapabilityAssignmentsSection'

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getCapabilityAssignments: vi.fn(), setCapabilityAssignment: vi.fn(), getModels: vi.fn() }
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
    ...overrides,
  }
}

const openai = makeProvider({})
const anthropic = makeProvider({
  id: 'provider-anthropic',
  providerKey: 'anthropic',
  displayName: 'Anthropic',
})

const unassigned: AiCapabilityAssignment = {
  capability: 'LocationIntent',
  providerId: null,
  modelId: null,
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
  // MUI opens the menu through a Popover transition, so the listbox is not in the DOM on the
  // same tick as the mouseDown.
  await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
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

  it('offers no "platform default" option — a capability is either assigned or it is not', async () => {
    renderSection([unassigned])

    const menu = await openProviderMenu('Location intent')

    expect(menu.queryByText('Platform default')).not.toBeInTheDocument()
  })

  it('prompts for a choice until one is made', async () => {
    renderSection([unassigned])

    expect(await screen.findByText('Please select AI provider')).toBeInTheDocument()
  })

  it('disables the control and says so when no provider can serve anything yet', async () => {
    // A fresh install: nothing enabled, credentialled and carrying a default model.
    renderSection([unassigned], [])

    expect(await screen.findByText('No AI provider available')).toBeInTheDocument()
    expect(document.querySelector('[role="combobox"]')?.getAttribute('aria-disabled')).toBe('true')
  })

  it('reports an unassigned capability as unassigned, not as running somewhere', async () => {
    // The server still resolves a fallback so nothing breaks mid-turn, but the screen must not
    // present that as configuration: an unassigned capability is a decision not yet made.
    renderSection([unassigned])

    expect(await screen.findByText('Not assigned')).toBeInTheDocument()
  })

  it('lists every provider that is enabled, credentialled and has a default model', async () => {
    const gemini = makeProvider({
      id: 'provider-gemini',
      providerKey: 'google-gemini',
      displayName: 'Google Gemini',
      defaultModelId: 'model-gemini',
    })
    renderSection([unassigned], [openai, anthropic, gemini])

    const menu = await openProviderMenu('Location intent')

    expect(menu.getByText('OpenAI')).toBeInTheDocument()
    expect(menu.getByText('Anthropic')).toBeInTheDocument()
    expect(menu.getByText('Google Gemini')).toBeInTheDocument()
  })

  it('lists Chat, which the server enumerates alongside the background capabilities', async () => {
    renderSection([{ ...unassigned, capability: 'Chat' }])

    expect(await screen.findByText('Chat')).toBeInTheDocument()
  })

  it('renders a capability it has no copy for instead of taking the page down', async () => {
    // The server enumerates the AiCapability enum. When "Chat" was added there before this
    // table knew about it, indexing straight into the copy map returned undefined and reading
    // `.label` threw during render — which the route error boundary turned into a full-page
    // "Something went wrong", with nothing to say the cause was a missing label.
    renderSection([{ ...unassigned, capability: 'SomethingNew' as AiCapabilityAssignment['capability'] }])

    expect(await screen.findByText('SomethingNew')).toBeInTheDocument()
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
    })
    renderSection([unassigned], [openai, openrouter])

    const menu = await openProviderMenu('Location intent')

    expect(menu.queryByText('OpenRouter')).not.toBeInTheDocument()
  })

  it('excludes a provider with no default model, since the model is what the capability runs on', async () => {
    // Assigning one would store a setting DefaultProviderResolver immediately falls back from.
    // Giving it a default model is the previous step, on the Default models page.
    const noDefaultModel = makeProvider({
      id: 'provider-gemini',
      providerKey: 'google-gemini',
      displayName: 'Google Gemini',
      defaultModelId: null,
    })
    renderSection([unassigned], [openai, noDefaultModel])

    const menu = await openProviderMenu('Location intent')

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

describe('CapabilityAssignmentsSection — image generation (specs/057 follow-up)', () => {
  function makeModel(overrides: Partial<AdminAiModel> & { imageOutput?: boolean }): AdminAiModel {
    const { imageOutput = false, ...rest } = overrides
    return {
      id: 'model-x',
      modelKey: 'x',
      displayName: 'X',
      contextWindowTokens: null,
      maxOutputTokens: null,
      capabilities: {
        streaming: false, vision: false, functionCalling: false, jsonMode: false, reasoning: false,
        embeddings: false, imageInput: false, imageOutput, audio: false,
      },
      pricing: null,
      releaseDate: null,
      status: 'Available',
      ...rest,
    }
  }

  const imageUnassigned: AiCapabilityAssignment = {
    capability: 'ImageGeneration',
    providerId: null,
    modelId: null,
    effectiveProviderId: null,
    effectiveModelId: null,
  }

  it('says it is not configured, and saves nothing until an image model is chosen', async () => {
    vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue([
      makeModel({ id: 'model-chat', displayName: 'GPT-5 (chat)' }),
      makeModel({ id: 'model-image', displayName: 'GPT Image 2', imageOutput: true }),
      makeModel({ id: 'model-retired', displayName: 'DALL-E 2', imageOutput: true, status: 'Deprecated' }),
    ])
    vi.mocked(adminAiProvidersApi.setCapabilityAssignment).mockResolvedValue(undefined)
    renderSection([imageUnassigned])

    expect(await screen.findByText('Not configured')).toBeInTheDocument()

    const providerMenu = await openProviderMenu('Image generation')
    fireEvent.click(providerMenu.getByText('OpenAI'))
    expect(adminAiProvidersApi.setCapabilityAssignment).not.toHaveBeenCalled()

    const modelSelect = await screen.findByRole('combobox', { name: 'Image model for Image generation' })
    // Disabled while the provider's models load — MUI ignores a press on a disabled select.
    await waitFor(() => expect(modelSelect).not.toHaveAttribute('aria-disabled', 'true'))
    fireEvent.mouseDown(modelSelect)
    await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
    const modelMenu = within(document.querySelector('ul[role="listbox"]') as HTMLElement)

    // Only Available, image-capable models are offered — never the chat model or a retired one.
    expect(modelMenu.queryByText('GPT-5 (chat)')).not.toBeInTheDocument()
    expect(modelMenu.queryByText('DALL-E 2')).not.toBeInTheDocument()
    fireEvent.click(modelMenu.getByText('GPT Image 2'))

    await waitFor(() =>
      expect(adminAiProvidersApi.setCapabilityAssignment).toHaveBeenCalledWith('ImageGeneration', 'provider-openai', 'model-image'),
    )
  })

  it('explains when the chosen provider has no image-capable model', async () => {
    vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue([makeModel({ id: 'model-chat', displayName: 'Claude' })])
    renderSection([imageUnassigned])

    const providerMenu = await openProviderMenu('Image generation')
    fireEvent.click(providerMenu.getByText('Anthropic'))

    expect(await screen.findByText(/no Available model marked as able to produce images/i)).toBeInTheDocument()
  })
})
