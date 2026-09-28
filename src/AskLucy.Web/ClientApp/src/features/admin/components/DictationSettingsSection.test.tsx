import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { DictationSettingsSection } from './DictationSettingsSection'
import { dictationSettings } from './dictationFixtures'

const recorder = vi.hoisted(() => ({ start: vi.fn(), stop: vi.fn() }))
vi.mock('../hooks/useWavSampleRecorder', async () => {
  const { useState } = await vi.importActual<typeof import('react')>('react')
  return {
    MAX_SAMPLE_SECONDS: 30,
    useWavSampleRecorder: () => {
      const [isRecording, setIsRecording] = useState(false)
      return {
        isRecording,
        start: async () => {
          await recorder.start()
          setIsRecording(true)
        },
        stop: async () => {
          setIsRecording(false)
          return recorder.stop() as Promise<Blob>
        },
      }
    },
  }
})

const MANAGE = ['admin.ai-providers.view', 'admin.ai-providers.manage']

function sessionWith(permissions: string[]) {
  return http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions }),
  )
}

const server = setupServer(
  sessionWith(MANAGE),
  http.get('*/api/v1/admin/voice/dictation', () => HttpResponse.json(dictationSettings())),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
beforeEach(() => {
  recorder.start.mockReset().mockResolvedValue(undefined)
  recorder.stop.mockReset().mockResolvedValue(new Blob(['RIFF'], { type: 'audio/wav' }))
})

function renderSection() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <DictationSettingsSection />
    </QueryClientProvider>,
  )
}

/** `hidden: true` skips the accessibility walk, whose getComputedStyle crashes jsdom while the menu is open. */
const openModelMenu = async () => {
  fireEvent.mouseDown(await screen.findByRole('combobox', { name: 'Model' }))
  return screen.getByRole('listbox', { hidden: true })
}

async function chooseModel(label: string) {
  fireEvent.click(within(await openModelMenu()).getByText(label))
  await waitFor(() => expect(screen.queryByRole('listbox', { hidden: true })).not.toBeInTheDocument())
}

describe('DictationSettingsSection', () => {
  it('explains a fresh deploy uses the browser built-in and lists the deployments', async () => {
    renderSection()

    expect(await screen.findByText(/No Local Whisper model is selected/)).toBeInTheDocument()
    const listbox = await openModelMenu()
    expect(within(listbox).getByText('No model (browser built-in)')).toBeInTheDocument()
    expect(within(listbox).getByText('whisper.cpp (ggml-base.bin)').closest('li')).not.toHaveAttribute('aria-disabled')
    expect(within(listbox).getByText('supertonic-3').closest('li')).toHaveAttribute('aria-disabled', 'true')
    expect(screen.getByText('supertonic-3: Deploy the model from a URL that names its .bin file.')).toBeInTheDocument()
  })

  it('warns when the selected model cannot serve', async () => {
    server.use(
      http.get('*/api/v1/admin/voice/dictation', () =>
        HttpResponse.json(
          dictationSettings({
            localWhisper: {
              ...dictationSettings().localWhisper,
              selectedModelId: 'model-base',
              effectiveModel: {
                label: 'whisper.cpp (ggml-base.bin)',
                ready: false,
                problem: 'The Local Whisper model file is missing, so dictation uses the browser built-in.',
              },
            },
          }),
        ),
      ),
    )
    renderSection()

    const alert = (await screen.findByText(/model file is missing/)).closest('[role="alert"]')!
    expect(alert.className).toMatch(/Warning/)
  })

  it('says which model Local Whisper uses when it is ready', async () => {
    server.use(
      http.get('*/api/v1/admin/voice/dictation', () =>
        HttpResponse.json(
          dictationSettings({
            localWhisper: {
              ...dictationSettings().localWhisper,
              selectedModelId: 'model-base',
              effectiveModel: { label: 'whisper.cpp (ggml-base.bin)', ready: true, problem: null },
            },
          }),
        ),
      ),
    )
    renderSection()

    expect(await screen.findByText('Local Whisper uses whisper.cpp (ggml-base.bin).')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Use this model' })).toBeDisabled()
  })

  it('selects a model with the row version and confirms it', async () => {
    let body: unknown
    server.use(
      http.put('*/api/v1/admin/voice/dictation/local-whisper-model', async ({ request }) => {
        body = await request.json()
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderSection()

    await chooseModel('whisper.cpp (ggml-base.bin)')
    fireEvent.click(screen.getByRole('button', { name: 'Use this model' }))

    expect(await screen.findByText('Local Whisper now uses whisper.cpp (ggml-base.bin).')).toBeInTheDocument()
    expect(body).toEqual({ customModelId: 'model-base', rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows why a selection was refused', async () => {
    server.use(
      http.put('*/api/v1/admin/voice/dictation/local-whisper-model', () =>
        HttpResponse.json(
          { title: 'Conflict', status: 409, detail: 'Someone else changed the dictation settings. Review them and try again.' },
          { status: 409 },
        ),
      ),
    )
    renderSection()

    await chooseModel('whisper.cpp (ggml-base.bin)')
    fireEvent.click(screen.getByRole('button', { name: 'Use this model' }))

    expect(await screen.findByText('Someone else changed the dictation settings. Review them and try again.')).toBeInTheDocument()
  })

  it('tries the chosen model on a recorded sample without selecting it', async () => {
    let putCalled = false
    let contentType: string | null = null
    server.use(
      http.put('*/api/v1/admin/voice/dictation/local-whisper-model', () => {
        putCalled = true
        return new HttpResponse(null, { status: 204 })
      }),
      http.post('*/api/v1/admin/voice/dictation/try', ({ request }) => {
        // Reading jsdom's multipart body never settles under MSW; adminVoiceApi.test.ts covers the parts.
        contentType = request.headers.get('content-type')
        return HttpResponse.json({ text: 'Hello Lucy', elapsedMs: 812, modelLabel: 'whisper.cpp (ggml-base.bin)' })
      }),
    )
    renderSection()

    await chooseModel('whisper.cpp (ggml-base.bin)')
    fireEvent.click(screen.getByRole('button', { name: 'Try it' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Stop and transcribe' }))

    expect(await screen.findByText('“Hello Lucy”')).toBeInTheDocument()
    expect(screen.getByText('whisper.cpp (ggml-base.bin) · 812 ms')).toBeInTheDocument()
    expect(contentType).toMatch(/^multipart\/form-data; boundary=/)
    expect(putCalled).toBe(false)
  })

  it('shows why a try failed', async () => {
    server.use(
      http.post('*/api/v1/admin/voice/dictation/try', () =>
        HttpResponse.json(
          { title: 'Provider unavailable', status: 503, detail: 'Local Whisper could not load this model.' },
          { status: 503 },
        ),
      ),
    )
    renderSection()

    await chooseModel('whisper.cpp (ggml-base.bin)')
    fireEvent.click(screen.getByRole('button', { name: 'Try it' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Stop and transcribe' }))

    expect(await screen.findByText('Local Whisper could not load this model.')).toBeInTheDocument()
  })

  it('shows why the microphone could not be opened', async () => {
    recorder.start.mockRejectedValue(new Error('Permission denied'))
    renderSection()

    await chooseModel('whisper.cpp (ggml-base.bin)')
    fireEvent.click(screen.getByRole('button', { name: 'Try it' }))

    expect(await screen.findByText('Permission denied')).toBeInTheDocument()
  })

  it('shows the model but no controls to a viewer', async () => {
    server.use(sessionWith(['admin.ai-providers.view']))
    renderSection()

    expect(await screen.findByText(/No Local Whisper model is selected/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Use this model' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try it' })).not.toBeInTheDocument()
  })
})
