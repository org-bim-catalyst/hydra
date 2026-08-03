import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as voiceApi from '../../chat/api/voiceApi'
import { useVoicePreferencesStore } from '../../chat/voice/voicePreferencesStore'
import { VoiceTab } from './SettingsPage'

vi.mock('../../chat/api/voiceApi')

const SERVER_PREFERENCE: voiceApi.UserVoicePreference = {
  conversationMode: 'PushToTalk',
  isMuted: false,
  selectedVoiceId: null,
  voiceSpeed: 1,
  voiceStyle: 0,
  preferredMicrophoneDeviceId: null,
  preferredSpeakerDeviceId: null,
}

function resetStore() {
  useVoicePreferencesStore.setState({ ...SERVER_PREFERENCE, error: null })
}

describe('VoiceTab', () => {
  beforeEach(() => {
    resetStore()
    vi.mocked(voiceApi.getVoicePreferences).mockResolvedValue(SERVER_PREFERENCE)
    vi.mocked(voiceApi.saveVoicePreferences).mockImplementation((preference) =>
      Promise.resolve(preference),
    )
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: { enumerateDevices: vi.fn().mockResolvedValue([]) },
    })
  })

  it('hydrates preferences from the server on mount', async () => {
    render(<VoiceTab />)

    await waitFor(() => expect(voiceApi.getVoicePreferences).toHaveBeenCalled())
  })

  it('saves an advanced voice id when edited', async () => {
    render(<VoiceTab />)
    await screen.findByLabelText(/voice id/i)

    fireEvent.change(screen.getByLabelText(/voice id/i), { target: { value: 'custom-voice' } })

    await waitFor(() =>
      expect(voiceApi.saveVoicePreferences).toHaveBeenCalledWith(
        expect.objectContaining({ selectedVoiceId: 'custom-voice' }),
      ),
    )
  })

  it('surfaces a save failure instead of failing silently', async () => {
    vi.mocked(voiceApi.saveVoicePreferences).mockRejectedValue(new Error('Save failed.'))
    render(<VoiceTab />)
    await screen.findByLabelText(/voice id/i)

    fireEvent.change(screen.getByLabelText(/voice id/i), { target: { value: 'x' } })

    expect(await screen.findByText('Save failed.')).toBeInTheDocument()
  })
})
