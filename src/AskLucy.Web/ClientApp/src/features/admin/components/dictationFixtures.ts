import type { DictationSettings } from '../api/adminVoiceApi'

/** Test fixture: a fresh deployment's dictation setting — Local Whisper primary, no model selected. */
export function dictationSettings(overrides: Partial<DictationSettings> = {}): DictationSettings {
  return {
    primaryEngine: 'LocalWhisper',
    pushToTalkEngine: 'LocalWhisper',
    state: 'Active',
    suspension: null,
    lastRevert: null,
    engines: [
      { engine: 'LocalWhisper', selectable: true, unavailableReason: null },
      { engine: 'OpenAiWhisper', selectable: false, unavailableReason: 'OpenAI is switched off under AI providers.' },
      { engine: 'ElevenLabsRealtime', selectable: true, unavailableReason: null },
    ],
    pushToTalkEngines: [
      { engine: 'LocalWhisper', selectable: true, unavailableReason: null },
      { engine: 'OpenAiWhisper', selectable: false, unavailableReason: 'OpenAI is switched off under AI providers.' },
      { engine: 'Browser', selectable: true, unavailableReason: null },
    ],
    localWhisper: {
      selectedModelId: null,
      effectiveModel: {
        label: null,
        ready: false,
        problem:
          'No Local Whisper model is selected, so dictation uses the browser built-in. Deploy ggml-base.bin under Custom Models, then select it here.',
      },
      models: [
        { id: 'model-base', label: 'whisper.cpp (ggml-base.bin)', selectable: true, reason: null },
        { id: 'model-supertonic', label: 'supertonic-3', selectable: false, reason: 'Deploy the model from a URL that names its .bin file.' },
      ],
    },
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides,
  }
}
