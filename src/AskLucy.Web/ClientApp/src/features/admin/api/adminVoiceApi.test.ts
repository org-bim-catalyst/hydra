import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../../../api/httpClient'
import { selectLocalWhisperModel, tryLocalWhisperModel } from './adminVoiceApi'

vi.mock('../../../api/httpClient', () => ({ apiFetch: vi.fn() }))

const fetchMock = vi.mocked(apiFetch)

beforeEach(() => {
  fetchMock.mockReset().mockResolvedValue(undefined)
})

describe('adminVoiceApi dictation', () => {
  it('sends the Local Whisper selection with its row version', async () => {
    await selectLocalWhisperModel(null, 'AAAAAAAAB9E=')

    expect(fetchMock).toHaveBeenCalledWith('/admin/voice/dictation/local-whisper-model', {
      method: 'PUT',
      body: JSON.stringify({ customModelId: null, rowVersion: 'AAAAAAAAB9E=' }),
    })
  })

  it('posts a try as a WAV file with the deployment and language', async () => {
    await tryLocalWhisperModel('model-base', new Blob(['RIFF'], { type: 'audio/wav' }), 'ar')

    const [path, init] = fetchMock.mock.calls[0]
    expect(path).toBe('/admin/voice/dictation/try')
    expect(init?.method).toBe('POST')
    const form = init?.body as FormData
    expect(form.get('customModelId')).toBe('model-base')
    expect(form.get('language')).toBe('ar')
    const file = form.get('file') as File
    expect(file.name).toBe('sample.wav')
    expect(file.type).toBe('audio/wav')
  })

  it('leaves the language out when none is given', async () => {
    await tryLocalWhisperModel('model-base', new Blob(['RIFF']))

    const form = fetchMock.mock.calls[0][1]?.body as FormData
    expect(form.has('language')).toBe(false)
  })
})
