import { apiFetch } from '../../../api/httpClient'
import type { GenerationParameters } from '../../chat/api/aiApi'

/** specs/005-multi-provider-ai-engine contracts/preferences.md */
export interface UserAiPreference {
  defaultProviderId: string
  defaultModelId: string
  defaultGenerationParameters: GenerationParameters | null
  isPlatformDefault: boolean
}

export const getPreferences = () => apiFetch<UserAiPreference>('/ai/preferences')

