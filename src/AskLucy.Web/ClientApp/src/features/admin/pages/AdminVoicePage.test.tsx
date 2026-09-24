import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminVoiceProvider, VoiceEngine, VoiceOption } from '../api/adminVoiceApi'
import { AdminVoicePage } from './AdminVoicePage'

const elevenLabs: AdminVoiceProvider = {
  id: 'provider-eleven',
  providerKey: 'ElevenLabs',
  displayName: 'ElevenLabs',
  priority: 0,
  isPrimary: true,
  defaultVoiceId: 'rachel',
  requiresCredential: true,
  hasCredential: true,
  credentialHint: 'sk_1...9XYZ',
  modelStatus: 'Ready',
  modelStatusReason: null,
  vendorEnabled: null,
}

const supertonic: AdminVoiceProvider = {
  id: 'provider-supertonic',
  providerKey: 'Supertonic',
  displayName: 'Supertonic (on-server)',
  priority: 1,
  isPrimary: false,
  defaultVoiceId: null,
  requiresCredential: false,
  hasCredential: false,
  credentialHint: null,
  modelStatus: 'Ready',
  modelStatusReason: null,
  vendorEnabled: null,
}

const voicesByProvider: Record<string, VoiceOption[]> = {
  [elevenLabs.id]: [
    { id: 'rachel', name: 'Rachel', gender: 'female', description: 'american, young' },
    { id: 'adam', name: 'Adam', gender: 'male', description: null },
  ],
  [supertonic.id]: [
    { id: 'F1', name: 'Female 1', gender: 'female', description: 'Supertonic 3 preset voice' },
    { id: 'F2', name: 'Female 2', gender: 'female', description: 'Supertonic 3 preset voice' },
  ],
}

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['Administrator'], permissions: [] }),
  ),
  http.get('*/api/v1/admin/voice/providers', () => HttpResponse.json([elevenLabs, supertonic])),
  http.get('*/api/v1/admin/voice/providers/:id/voices', ({ params }) =>
    HttpResponse.json(voicesByProvider[params.id as string] ?? []),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const play = vi.fn(() => Promise.resolve())

beforeEach(() => {
  play.mockClear()
  vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(play)
  vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => {})
  URL.createObjectURL = vi.fn(() => 'blob:sample')
  URL.revokeObjectURL = vi.fn()
})

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminVoicePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/** A button found by its text. Role queries on this page trip the same jsdom bug as the menus. */
const buttonWithText = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement

/**
 * Options are picked by text inside the open listbox, not by role — role queries on MUI's
 * portalled menu trip a jsdom getComputedStyle bug (see CapabilityAssignmentsSection.test.tsx).
 */
async function choose(comboboxName: string, optionText: string | RegExp) {
  fireEvent.mouseDown(await screen.findByLabelText(comboboxName, { selector: '[role="combobox"]' }))
  await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
  fireEvent.click(within(document.querySelector('ul[role="listbox"]') as HTMLElement).getByText(optionText))
  await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).toBeNull())
}

describe('AdminVoicePage (specs/070)', () => {
  it("opens on Lucy's current provider and voice", async () => {
    renderPage()

    await waitFor(() =>
      expect(screen.getByLabelText('Voice provider', { selector: '[role="combobox"]' })).toHaveTextContent("ElevenLabs — Lucy's voice"),
    )
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))
    expect(screen.getByText("Lucy's voice")).toBeInTheDocument()
    expect(screen.getByText(/sk_1\.\.\.9XYZ/)).toBeInTheDocument()
  })

  it("lists the chosen provider's own voices", async () => {
    renderPage()
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))

    await choose('Voice provider', /Supertonic/)

    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Female 1'))
    expect(buttonWithText("Set as Lucy's voice")).toBeEnabled()
    expect(screen.getByText('Lucy currently speaks with ElevenLabs.')).toBeInTheDocument()
  })

  it('speaks the sample sentence with the selected voice and plays it', async () => {
    let body: unknown
    server.use(
      http.post('*/api/v1/admin/voice/providers/:id/preview', async ({ request, params }) => {
        body = { providerId: params.id, ...((await request.json()) as object) }
        return HttpResponse.json({ audioBase64: btoa('mp3-bytes'), contentType: 'audio/mpeg' })
      }),
    )
    renderPage()
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))
    await choose('Voice', 'Adam')
    fireEvent.change(screen.getByLabelText('Sample sentence'), { target: { value: 'Testing one two.' } })

    fireEvent.click(buttonWithText('Play'))

    await waitFor(() => expect(play).toHaveBeenCalledTimes(1))
    expect(body).toEqual({ providerId: elevenLabs.id, voiceId: 'adam', text: 'Testing one two.', language: 'en' })
    expect(await screen.findByText('Stop')).toBeInTheDocument()
  })

  it('swaps the untouched sample sentence when the language changes', async () => {
    renderPage()
    const sample = await screen.findByLabelText('Sample sentence')
    expect(sample).toHaveValue("Hello, I'm Lucy. How can I help you today?")

    await choose('Language', 'Arabic')

    expect(screen.getByLabelText('Sample sentence')).toHaveValue('مرحبا، أنا لوسي. كيف يمكنني مساعدتك اليوم؟')
  })

  it("shows the server's reason when the sample cannot be spoken", async () => {
    server.use(
      http.post('*/api/v1/admin/voice/providers/:id/preview', () =>
        HttpResponse.json(
          { title: 'Provider unavailable', status: 503, detail: 'The Supertonic voice model is not installed on this server.' },
          { status: 503 },
        ),
      ),
    )
    renderPage()
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))

    fireEvent.click(buttonWithText('Play'))

    expect(await screen.findByText('The Supertonic voice model is not installed on this server.')).toBeInTheDocument()
    expect(play).not.toHaveBeenCalled()
  })

  describe('a provider whose custom model is unavailable (specs/072 FR-037)', () => {
    const reason = 'The model deployed from Supertone/supertonic-3 is marked unavailable in Custom Models.'
    const unavailable: AdminVoiceProvider = { ...supertonic, modelStatus: 'ModelUnavailable', modelStatusReason: reason }

    beforeEach(() => {
      server.use(http.get('*/api/v1/admin/voice/providers', () => HttpResponse.json([elevenLabs, unavailable])))
    })

    it('shows a "model unavailable" chip with the reason as its tooltip', async () => {
      renderPage()
      await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))
      expect(screen.queryByText('Model unavailable')).not.toBeInTheDocument()

      await choose('Voice provider', /Supertonic/)

      const chip = await screen.findByText('Model unavailable')
      expect(chip.closest('[title]')).toHaveAttribute('title', reason)
      expect(screen.getByText('Replies fail over to the next voice provider.')).toBeInTheDocument()
    })

    it("surfaces the server's reason when its preview fails", async () => {
      server.use(
        http.post('*/api/v1/admin/voice/providers/:id/preview', () =>
          HttpResponse.json(
            { title: 'Provider unavailable', status: 503, detail: 'The Supertonic model is marked unavailable in Custom Models.' },
            { status: 503 },
          ),
        ),
      )
      renderPage()
      await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))
      await choose('Voice provider', /Supertonic/)
      await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Female 1'))

      fireEvent.click(buttonWithText('Play'))

      expect(await screen.findByText('The Supertonic model is marked unavailable in Custom Models.')).toBeInTheDocument()
      expect(play).not.toHaveBeenCalled()
    })
  })

  it('shows a retryable error when the voices cannot be listed', async () => {
    server.use(
      http.get('*/api/v1/admin/voice/providers/:id/voices', () =>
        HttpResponse.json({ title: 'Unauthorized', status: 502, detail: 'ElevenLabs rejected the API key.' }, { status: 502 }),
      ),
    )
    renderPage()

    expect(await screen.findByText('ElevenLabs rejected the API key.')).toBeInTheDocument()
    expect(buttonWithText('Retry')).toBeInTheDocument()
  })

  it("makes the selected voice Lucy's voice", async () => {
    let body: unknown
    server.use(
      http.put('*/api/v1/admin/voice/primary', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json([
          { ...supertonic, priority: 0, isPrimary: true, defaultVoiceId: 'F2' },
          { ...elevenLabs, priority: 1, isPrimary: false },
        ])
      }),
    )
    renderPage()
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))
    await choose('Voice provider', /Supertonic/)
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Female 1'))
    await choose('Voice', /^Female 2/)

    fireEvent.click(buttonWithText("Set as Lucy's voice"))

    expect(await screen.findByText('Lucy now speaks with Supertonic (on-server) — Female 2.')).toBeInTheDocument()
    expect(body).toEqual({ providerId: supertonic.id, voiceId: 'F2' })
    expect(screen.getByText("Lucy's voice")).toBeInTheDocument()
  })

  it('adds a provider from the + button and selects it', async () => {
    const engines: VoiceEngine[] = [
      { providerKey: 'ElevenLabs', displayName: 'ElevenLabs', requiresCredential: true, isAdded: true, isKeyedAsAiProvider: false },
      { providerKey: 'Supertonic', displayName: 'Supertonic (on-server)', requiresCredential: false, isAdded: false, isKeyedAsAiProvider: false },
    ]
    let body: unknown
    let added = false
    server.use(
      http.get('*/api/v1/admin/voice/providers', () =>
        HttpResponse.json(added ? [elevenLabs, supertonic] : [elevenLabs]),
      ),
      http.get('*/api/v1/admin/voice/engines', () => HttpResponse.json(engines)),
      http.post('*/api/v1/admin/voice/providers', async ({ request }) => {
        body = await request.json()
        added = true
        return HttpResponse.json(supertonic, { status: 201 })
      }),
    )
    renderPage()
    await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'))

    fireEvent.click(screen.getByLabelText('Add voice provider'))
    await screen.findByText('Add voice provider', { selector: 'h2' })
    // Inside the open dialog, query by text: role queries hit jsdom's getComputedStyle bug.
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement
    await waitFor(() => expect(within(dialog).getByText('Provider', { selector: 'label' })).toBeInTheDocument())
    fireEvent.mouseDown(dialog.querySelector('[role="combobox"]') as HTMLElement)
    await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
    const menu = within(document.querySelector('ul[role="listbox"]') as HTMLElement)
    expect(menu.queryByText('ElevenLabs')).toBeNull()
    fireEvent.click(menu.getByText('Supertonic (on-server)'))
    fireEvent.click(within(dialog).getByText('Add'))

    expect(await screen.findByText('Supertonic (on-server) added.')).toBeInTheDocument()
    expect(body).toEqual({ providerKey: 'Supertonic', apiKey: null })
    await waitFor(() =>
      expect(screen.getByLabelText('Voice provider', { selector: '[role="combobox"]' })).toHaveTextContent('Supertonic (on-server) — failover 1'),
    )
  })

  describe('an engine keyed under AI providers', () => {
    it('shows its on/off state and links to AI providers instead of offering a key', async () => {
      server.use(
        http.get('*/api/v1/admin/voice/providers', () =>
          HttpResponse.json([{ ...elevenLabs, vendorEnabled: false }, supertonic]),
        ),
      )
      renderPage()

      expect(await screen.findByText('Manage under AI providers')).toHaveAttribute('href', '/admin/ai-providers')
      expect(screen.getByText('Off')).toBeInTheDocument()
      expect(screen.getByText(/Switched off — replies fail over/)).toBeInTheDocument()
      expect(screen.queryByText('Replace key')).toBeNull()
      expect(screen.queryByText('Set key')).toBeNull()
    })

    it('asks for no key when adding it, and sends none', async () => {
      const engines: VoiceEngine[] = [
        { providerKey: 'ElevenLabs', displayName: 'ElevenLabs', requiresCredential: true, isAdded: false, isKeyedAsAiProvider: true },
      ]
      let body: unknown
      server.use(
        http.get('*/api/v1/admin/voice/providers', () => HttpResponse.json([supertonic])),
        http.get('*/api/v1/admin/voice/engines', () => HttpResponse.json(engines)),
        http.post('*/api/v1/admin/voice/providers', async ({ request }) => {
          body = await request.json()
          return HttpResponse.json({ ...elevenLabs, priority: 1, isPrimary: false, vendorEnabled: true }, { status: 201 })
        }),
      )
      renderPage()
      await waitFor(() => expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Female 1'))

      fireEvent.click(screen.getByLabelText('Add voice provider'))
      await screen.findByText('Add voice provider', { selector: 'h2' })
      const dialog = document.querySelector('[role="dialog"]') as HTMLElement
      await waitFor(() => expect(within(dialog).getByText('Provider', { selector: 'label' })).toBeInTheDocument())
      fireEvent.mouseDown(dialog.querySelector('[role="combobox"]') as HTMLElement)
      await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
      fireEvent.click(within(document.querySelector('ul[role="listbox"]') as HTMLElement).getByText('ElevenLabs'))

      expect(within(dialog).getByText(/uses the API key set under Admin → AI providers/)).toBeInTheDocument()
      expect(within(dialog).queryByText('API key', { selector: 'label' })).toBeNull()
      fireEvent.click(within(dialog).getByText('Add'))

      await waitFor(() => expect(body).toEqual({ providerKey: 'ElevenLabs', apiKey: null }))
    })
  })
})
